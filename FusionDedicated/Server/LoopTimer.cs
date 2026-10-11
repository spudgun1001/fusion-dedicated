namespace FusionDedicated.Server;

/// <summary>
/// Times one pass of the main loop and says where a slow one spent its time, at most once every
/// ten seconds, so a hitch every player feels leaves a trace in the log.
/// </summary>
public sealed class LoopTimer
{
    public const double SlowMs = 50;

    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(10);

    private readonly List<(string Name, double Ms)> _phases = new();
    private int _messages;
    private (byte Tag, string Sender, double Ms) _slowest;
    private DateTime _lastLogged = DateTime.MinValue;
    private int _heldBack;

    /// <summary>The slowest message so far this pass, so a caller names a sender only when it matters.</summary>
    public double SlowestMs => _slowest.Ms;

    public void Message(byte tag, string sender, double ms)
    {
        _messages++;
        if (ms > _slowest.Ms) _slowest = (tag, sender, ms);
    }

    public void Phase(string name, double ms) => _phases.Add((name, ms));

    /// <summary>The line to log for this pass, or null when it was quick or one was logged moments ago.</summary>
    public string? End(double totalMs, double lockWaitMs, int collections, DateTime now)
    {
        string? line = null;

        if (totalMs >= SlowMs)
        {
            if (now - _lastLogged < Quiet)
            {
                _heldBack++;
            }
            else
            {
                var parts = _phases.Where(p => p.Ms >= 1).Select(p => p.Name == "receive" && _messages > 0
                    ? $"receive {p.Ms:0} ms over {_messages} message(s), slowest tag {_slowest.Tag} from {_slowest.Sender} {_slowest.Ms:0} ms"
                    : $"{p.Name} {p.Ms:0} ms");
                line = $"Slow loop pass: {totalMs:0} ms ({string.Join("; ", parts.Append($"lock wait {lockWaitMs:0} ms").Append($"{collections} GC"))})"
                       + (_heldBack > 0 ? $"; {_heldBack} more slow pass(es) held back" : "");
                _lastLogged = now;
                _heldBack = 0;
            }
        }

        _phases.Clear();
        _messages = 0;
        _slowest = default;
        return line;
    }
}
