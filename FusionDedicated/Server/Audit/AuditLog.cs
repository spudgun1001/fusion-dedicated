using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Audit;

public enum AuditChannel
{
    Console,
    Rcon,
    Panel,
    InGame,
    Automatic,
}

public sealed class AuditEntry
{
    [JsonPropertyName("at")]
    public DateTime At { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("channel")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AuditChannel Channel { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("targetId")]
    public ulong TargetId { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

/// <summary>
/// A record of who was moderated, how and through which channel. Kept apart from
/// the server log so it survives rotation and reads on its own. One JSON object per
/// line, so a truncated write costs one entry rather than the file.
/// </summary>
public sealed class AuditLog
{
    public const string FileName = "moderation.log";

    private static readonly JsonSerializerOptions Options = new();

    private readonly string _path;
    private readonly object _lock = new();

    public AuditLog(string directory)
    {
        _path = Path.Combine(directory, FileName);
    }

    public void Record(AuditChannel channel, string action, string target, ulong targetId, string reason)
    {
        var entry = new AuditEntry
        {
            Channel = channel,
            Action = action,
            Target = target,
            TargetId = targetId,
            Reason = reason,
        };

        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
                File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
            }
        }
        catch
        {
            // An audit trail must never be able to stop a moderation action.
        }
    }

    /// <summary>The newest entries first. Unreadable lines are skipped.</summary>
    public IReadOnlyList<AuditEntry> Recent(int count)
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_path))
                {
                    return Array.Empty<AuditEntry>();
                }

                var entries = new List<AuditEntry>();

                foreach (string line in File.ReadLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        if (JsonSerializer.Deserialize<AuditEntry>(line, Options) is { } entry)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip the line, keep the log.
                    }
                }

                entries.Reverse();

                return entries.Take(count).ToList();
            }
        }
        catch
        {
            return Array.Empty<AuditEntry>();
        }
    }
}
