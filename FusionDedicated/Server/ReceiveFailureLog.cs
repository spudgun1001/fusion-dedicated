namespace FusionDedicated.Server;

/// <summary>
/// Says a native receive failure once, then at most once a minute with a count,
/// so a socket that keeps failing cannot fill the log. Only the loop calls it.
/// </summary>
public sealed class ReceiveFailureLog
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(1);

    private DateTime? _lastReport;
    private int _sinceReport;

    /// <returns>The line to log now, or null to stay quiet.</returns>
    public string? Failed(int code, DateTime now)
    {
        _sinceReport++;

        if (_lastReport is { } last && now - last < Quiet)
        {
            return null;
        }

        string line = _lastReport == null
            ? $"Steam could not read from the poll group (code {code})"
            : $"Steam could not read from the poll group {_sinceReport} times since the last report (last code {code})";

        _lastReport = now;
        _sinceReport = 0;

        return line;
    }
}
