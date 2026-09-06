using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-pluginstore-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "data.json");

    public PluginStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed record Roster(List<ulong> Members);

    [Fact]
    public void A_value_comes_back_as_it_went_in()
    {
        var store = new PluginStore(Path_);
        store.Set("roster", new Roster(new List<ulong> { 1, 2 }));

        Assert.Equal(new List<ulong> { 1, 2 }, store.Get<Roster>("roster")!.Members);
    }

    [Fact]
    public void A_key_that_was_never_set_comes_back_as_nothing()
    {
        Assert.Null(new PluginStore(Path_).Get<Roster>("missing"));
    }

    [Fact]
    public void Values_survive_a_restart()
    {
        var store = new PluginStore(Path_);
        store.Set("roster", new Roster(new List<ulong> { 7 }));
        store.Save();

        var reopened = new PluginStore(Path_);
        reopened.Load();

        Assert.Equal(new List<ulong> { 7 }, reopened.Get<Roster>("roster")!.Members);
    }

    [Fact]
    public void A_key_can_be_removed()
    {
        var store = new PluginStore(Path_);
        store.Set("roster", new Roster(new List<ulong> { 1 }));

        Assert.True(store.Remove("roster"));
        Assert.Null(store.Get<Roster>("roster"));
    }

    [Fact]
    public void A_file_that_will_not_parse_leaves_what_is_already_loaded()
    {
        var store = new PluginStore(Path_);
        store.Set("roster", new Roster(new List<ulong> { 1 }));
        store.Save();

        File.WriteAllText(Path_, "{ not json");
        store.Load();

        Assert.Equal(new List<ulong> { 1 }, store.Get<Roster>("roster")!.Members);
    }

    [Fact]
    public void Loading_a_file_that_is_not_there_yet_is_not_an_error()
    {
        var store = new PluginStore(Path.Combine(_dir, "never-written.json"));

        store.Load();

        Assert.Null(store.Get<Roster>("roster"));
    }

    [Fact]
    public void Saving_creates_the_directory_it_needs()
    {
        var store = new PluginStore(Path.Combine(_dir, "nested", "deeper", "data.json"));
        store.Set("roster", new Roster(new List<ulong> { 1 }));

        store.Save();

        Assert.True(File.Exists(Path.Combine(_dir, "nested", "deeper", "data.json")));
    }
}
