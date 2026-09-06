namespace FusionDedicated.Plugins;

/// <summary>One module message the server did not know what to do with.</summary>
public sealed record ModuleSighting(
    string At, long HandlerTag, byte SmallId, string Sender, int Length, string Payload);

/// <summary>
/// Records module messages nothing handled, so somebody writing a plugin to host
/// a client mod can see what its messages contain. The tag alone names the door
/// without showing what came through it.
///
/// Off by default, held in memory only, and cleared when turned off, because a
/// payload may carry player content and this is a debugging tool rather than a
/// record.
/// </summary>
public sealed class ModuleInspector
{
    /// <summary>Enough to see a pattern, few enough to never matter for memory.</summary>
    public const int Capacity = 200;

    /// <summary>Longer than any message worth reading by hand.</summary>
    public const int MaxPayloadBytes = 256;

    private readonly LinkedList<ModuleSighting> _seen = new();
    private readonly object _lock = new();
    private bool _enabled;

    public bool Enabled
    {
        get { lock (_lock) { return _enabled; } }

        set
        {
            lock (_lock)
            {
                _enabled = value;

                // Turning it off forgets what was held, so it cannot be used to
                // read traffic captured before anybody looked.
                _seen.Clear();
            }
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ModuleSighting> Recent
    {
        get { lock (_lock) { return _seen.ToList(); } }
    }

    public void Note(long handlerTag, byte smallId, string sender, byte[] payload)
    {
        lock (_lock)
        {
            if (!_enabled)
            {
                return;
            }

            int kept = Math.Min(payload.Length, MaxPayloadBytes);

            _seen.AddFirst(new ModuleSighting(
                DateTime.UtcNow.ToLocalTime().ToString("HH:mm:ss"),
                handlerTag,
                smallId,
                sender,
                payload.Length,
                Convert.ToHexString(payload.AsSpan(0, kept))));

            while (_seen.Count > Capacity)
            {
                _seen.RemoveLast();
            }
        }
    }
}
