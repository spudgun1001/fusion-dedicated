namespace FusionDedicated.Server;

/// <summary>
/// Decides when to try publishing the lobby again. Steam can take minutes to
/// sign in, and a server that only asked once at startup stayed invisible for
/// the rest of its life when that happened.
/// </summary>
public sealed class LobbyRetry
{
    private readonly TimeSpan _interval;
    private DateTime _lastAttempt;

    public LobbyRetry(TimeSpan interval, DateTime? startedAt = null)
    {
        _interval = interval;
        _lastAttempt = startedAt ?? DateTime.UtcNow;
    }

    public bool ShouldRetry(bool published, DateTime now)
    {
        if (published)
        {
            // Keep the clock with the present, so losing a lobby does not fire
            // a burst of attempts for however long it was up.
            _lastAttempt = now;
            return false;
        }

        if (now - _lastAttempt < _interval)
        {
            return false;
        }

        _lastAttempt = now;
        return true;
    }
}
