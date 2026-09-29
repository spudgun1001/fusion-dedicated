using System.Text;
using FusionDedicated.Plugins;

namespace FusionDedicated.Web;

/// <summary>The gate's answer. Abort means the caller stalled, so the panel drops the connection rather than answering.</summary>
public readonly record struct PluginHttpGateReply(PluginHttpReply Reply, bool Abort)
{
    public int Status => Reply.Status;

    public string Json => Reply.Json;

    public static implicit operator PluginHttpGateReply(PluginHttpReply reply) => new(reply, false);
}

/// <summary>
/// The decisions behind a plugin HTTP route: which plugin and route a path names,
/// whether the caller may reach it, and the body size cap. Kept apart from
/// <see cref="Dashboard"/> so the boundary a request actually crosses is tested
/// directly rather than only through Dashboard.cs's source text.
/// </summary>
public static class PluginHttpGate
{
    public const int MaxBody = 16 * 1024;

    public static PluginHttpGateReply Handle(PluginHttp? http, PanelRole role, string actor, string method, string path,
        IReadOnlyDictionary<string, string> query, long contentLength, Stream body, TimeSpan readLimit)
    {
        string rest = path[PanelPermissions.PluginHttpPrefix.Length..];
        int slash = rest.IndexOf('/');
        string plugin = slash > 0 ? rest[..slash] : rest;
        string route = slash > 0 ? rest[(slash + 1)..] : "";

        if (http?.RoleFor(plugin, route) is not { } required)
        {
            return PluginHttpReply.Error(404, "No such route");
        }

        if (!PanelPermissions.MayCall(role, required))
        {
            return PluginHttpReply.Error(403, "not allowed");
        }

        if (contentLength > MaxBody)
        {
            return PluginHttpReply.Error(413, "Too big");
        }

        // The panel answers one request at a time, so a caller that stalls mid-body must not hold it.
        // Its own thread, so a busy thread pool cannot make a body that already arrived look stalled.
        var read = Task.Factory.StartNew(() => Read(body), TaskCreationOptions.LongRunning);

        if (Task.WaitAny(new Task[] { read }, readLimit) < 0)
        {
            return new PluginHttpGateReply(PluginHttpReply.Error(408, "Too slow"), true);
        }

        if (read.GetAwaiter().GetResult() is not { } text)
        {
            return PluginHttpReply.Error(413, "Too big");
        }

        return http.Invoke(plugin, route, new PluginHttpRequest(method, route, query, text, actor));
    }

    /// <summary>The body, or null when it is over the cap in bytes.</summary>
    private static string? Read(Stream body)
    {
        var buffer = new byte[MaxBody + 1];
        int read = body.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

        if (read > MaxBody) return null;

        var text = buffer.AsSpan(0, read);
        return Encoding.UTF8.GetString(text.StartsWith(Encoding.UTF8.Preamble) ? text[Encoding.UTF8.Preamble.Length..] : text);
    }
}
