namespace FusionDedicated.Server;

/// <summary>
/// Keeps one lobby publish in flight at a time, without the main loop ever
/// waiting on it.
///
/// This exists because awaiting the publish inside the loop deadlocks the whole
/// server. Steamworks only completes a call result when SteamAPI.RunCallbacks is
/// pumped, the main loop is the only thing pumping it once startup is over, and
/// an await suspends that loop. The task would then wait for a callback that
/// nothing can raise: no packets received, no ticks, every player frozen, and no
/// way out but a restart.
///
/// So the publish is started and left running. The loop carries on pumping,
/// the callback lands, and the result is collected on a later pass.
/// </summary>
public sealed class LobbyPublishGate
{
    private readonly TimeSpan _giveUpAfter;

    private Task<bool>? _publishing;
    private DateTime _startedAt;

    /// <param name="giveUpAfter">
    /// How long to wait before abandoning an attempt and allowing another. Steam
    /// can simply never answer, and one wedged call must not mean the lobby is
    /// never published again for the life of the process.
    /// </param>
    public LobbyPublishGate(TimeSpan? giveUpAfter = null)
        => _giveUpAfter = giveUpAfter ?? TimeSpan.FromSeconds(60);

    public bool InFlight => _publishing != null;

    /// <summary>True when there is room to start another attempt.</summary>
    public bool MayStart(DateTime now)
    {
        if (_publishing == null)
        {
            return true;
        }

        if (now - _startedAt < _giveUpAfter)
        {
            return false;
        }

        // Abandoned rather than cancelled: there is no cancelling a Steam call
        // result, so it is left to finish into nothing.
        _publishing = null;
        return true;
    }

    public void Started(Task<bool> publishing, DateTime now)
    {
        _publishing = publishing;
        _startedAt = now;
    }

    /// <summary>
    /// The result of an attempt that has finished, or null while one is still
    /// running or none has been made. Reading it clears it.
    /// </summary>
    public bool? Collect()
    {
        if (_publishing is not { IsCompleted: true } finished)
        {
            return null;
        }

        _publishing = null;

        // A faulted or cancelled attempt counts as a failure rather than throwing
        // out of the main loop, which would end the process.
        return finished.IsCompletedSuccessfully && finished.Result;
    }
}
