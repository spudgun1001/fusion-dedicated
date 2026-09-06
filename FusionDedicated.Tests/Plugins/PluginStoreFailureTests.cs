using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// What happens when a plugin's data.json cannot be written.
///
/// This is not hypothetical. A live server had plugins/labrp/data.json owned by
/// somebody else, and `plugins reload` died on it: the store threw out of
/// Unload, ReloadAll never reached LoadAll, and the old assembly stayed loaded
/// with nothing on the panel to say why.
/// </summary>
public class PluginStoreFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-store-" + Guid.NewGuid().ToString("N"));

    public PluginStoreFailureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>A path that cannot be written, because a directory sits on it.</summary>
    private string Blocked()
    {
        string path = Path.Combine(_dir, "data.json");
        Directory.CreateDirectory(path);

        return path;
    }

    [Fact]
    public void A_store_that_cannot_be_written_does_not_throw()
    {
        var store = new PluginStore(Blocked());
        store.Set("roster", new[] { 1, 2, 3 });

        // The whole fault: this used to throw, and it is called from Unload.
        Assert.False(store.Save());
    }

    [Fact]
    public void It_says_so_rather_than_failing_silently()
    {
        var lines = new List<string>();
        var store = new PluginStore(Blocked(), (level, message) => lines.Add($"{level} {message}"));

        store.Save();

        Assert.Single(lines);
        Assert.StartsWith("WARN", lines[0]);
        Assert.Contains("data.json", lines[0]);
    }

    [Fact]
    public void It_says_so_once_rather_than_on_every_change()
    {
        // A plugin saves on every button press and on every payday.
        var lines = new List<string>();
        var store = new PluginStore(Blocked(), (level, message) => lines.Add(message));

        for (int i = 0; i < 20; i++)
        {
            store.Save();
        }

        Assert.Single(lines);
    }

    [Fact]
    public void The_values_are_still_there_in_memory()
    {
        var store = new PluginStore(Blocked());
        store.Set("salary", 750);
        store.Save();

        Assert.Equal(750, store.Get<int>("salary"));
    }

    [Fact]
    public void Writable_says_which_state_it_is_in()
    {
        var store = new PluginStore(Blocked());
        Assert.True(store.Writable);

        store.Save();
        Assert.False(store.Writable);
    }

    [Fact]
    public void A_store_that_can_be_written_saves_and_says_nothing()
    {
        var lines = new List<string>();
        var store = new PluginStore(
            Path.Combine(_dir, "fine.json"), (level, message) => lines.Add(message));

        store.Set("salary", 250);

        Assert.True(store.Save());
        Assert.True(store.Writable);
        Assert.Empty(lines);
    }

    [Fact]
    public void Recovering_is_noticed_and_said()
    {
        string path = Path.Combine(_dir, "recover.json");
        Directory.CreateDirectory(path);

        var lines = new List<string>();
        var store = new PluginStore(path, (level, message) => lines.Add($"{level} {message}"));

        store.Set("salary", 100);
        Assert.False(store.Save());

        Directory.Delete(path);

        Assert.True(store.Save());
        Assert.Contains(lines, l => l.StartsWith("INFO") && l.Contains("can be written again"));
    }

    [Fact]
    public void A_folder_that_is_not_there_yet_is_made()
    {
        var store = new PluginStore(Path.Combine(_dir, "nested", "deeper", "data.json"));
        store.Set("x", 1);

        Assert.True(store.Save());
    }
}
