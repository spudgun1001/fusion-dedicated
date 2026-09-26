namespace FusionDedicated.Server;

/// <summary>
/// Decides when to look for our lobby the way Fusion's browser does, and when a
/// miss is worth a fresh lobby. Steam can keep the lobby while leaving it out of the
/// browser's list, and nothing else notices that.
/// </summary>
public sealed class LobbyVisibility
{
    private static readonly TimeSpan RepublishGap = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _interval;
    private DateTime _lastCheck;
    private DateTime _lastRepublish = DateTime.MinValue;
    private int _misses;

    public LobbyVisibility(TimeSpan interval, DateTime startedAt)
    {
        _interval = interval;
        _lastCheck = startedAt;
    }

    public bool Due(DateTime now)
    {
        if (_interval <= TimeSpan.Zero || now - _lastCheck < _interval)
        {
            return false;
        }

        _lastCheck = now;
        return true;
    }

    /// <summary>
    /// Gone by code means gone, so that republishes at once. Missing only from the
    /// browser needs two checks in a row and waits 15 minutes between republishes.
    /// </summary>
    public bool ShouldRepublish(bool inBrowser, bool byCode, DateTime now)
    {
        if (inBrowser)
        {
            _misses = 0;
            return false;
        }

        if (byCode && (++_misses < 2 || now - _lastRepublish < RepublishGap))
        {
            return false;
        }

        _misses = 0;
        _lastRepublish = now;
        return true;
    }
}
