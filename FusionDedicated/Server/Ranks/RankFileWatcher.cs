namespace FusionDedicated.Server.Ranks;

/// <summary>
/// Polls ranks.json so an SFTP edit applies without a restart. Polling beats change
/// notifications, which are unreliable across a container's bind mounts.
/// </summary>
public sealed class RankFileWatcher : IDisposable
{
    private readonly RankStore _store;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();

    public RankFileWatcher(RankStore store, Action<string> log)
    {
        _store = store;
        _log = log;
    }

    public void Start()
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    if (_store.ReloadIfChanged())
                    {
                        _log($"Reloaded ranks.json — {_store.Entries.Count} players listed");
                    }
                }
                catch (Exception ex)
                {
                    _log($"Could not reload ranks.json: {ex.Message}");
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
