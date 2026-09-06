namespace FusionDedicated.Plugins;

/// <summary>
/// Counts how often a plugin has thrown. One that keeps throwing is taken out of
/// the way rather than left to fill the log on every event.
/// </summary>
public sealed class PluginHealth
{
    public const int FailuresBeforeDisable = 3;

    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public bool IsDisabled(string plugin)
    {
        lock (_lock)
        {
            return _failures.TryGetValue(plugin, out int count)
                && count >= FailuresBeforeDisable;
        }
    }

    /// <summary>Records a failure. True only on the failure that disables it.</summary>
    public bool NoteFailure(string plugin)
    {
        lock (_lock)
        {
            _failures.TryGetValue(plugin, out int count);

            if (count >= FailuresBeforeDisable)
            {
                return false;
            }

            count++;
            _failures[plugin] = count;

            return count >= FailuresBeforeDisable;
        }
    }

    /// <summary>Clears a plugin's record, so reloading gives it a fresh start.</summary>
    public void Forget(string plugin)
    {
        lock (_lock)
        {
            _failures.Remove(plugin);
        }
    }
}
