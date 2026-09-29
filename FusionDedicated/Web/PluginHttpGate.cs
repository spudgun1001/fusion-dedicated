using System.Text;
using FusionDedicated.Plugins;

namespace FusionDedicated.Web;

/// <summary>
/// The decisions behind a plugin HTTP route: which plugin and route a path names,
/// whether the caller may reach it, and the body size cap. Kept apart from
/// <see cref="Dashboard"/> so the boundary a request actually crosses is tested
/// directly rather than only through Dashboard.cs's source text.
/// </summary>
public static class PluginHttpGate
{
    public const int MaxBody = 16 * 1024;

    public static PluginHttpReply Handle(PluginHttp? http, PanelRole role, string actor, string method, string path,
        IReadOnlyDictionary<string, string> query, long contentLength, Stream body)
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

        string text;
        using (var reader = new StreamReader(body, Encoding.UTF8))
        {
            var buffer = new char[MaxBody + 1];
            int read = reader.ReadBlock(buffer, 0, buffer.Length);

            if (read > MaxBody)
            {
                return PluginHttpReply.Error(413, "Too big");
            }

            text = new string(buffer, 0, read);
        }

        return http.Invoke(plugin, route, new PluginHttpRequest(method, route, query, text, actor));
    }
}
