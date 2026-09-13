using System.Collections.Concurrent;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>A store saved from several threads at once, and a key a plugin can no longer read.</summary>
public class PluginStoreSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-storesafety-" + Guid.NewGuid().ToString("N"));

    public PluginStoreSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Path_ => Path.Combine(_dir, "shops.json");

    [Fact]
    public void Saves_from_many_threads_all_succeed_and_leave_no_temporary_file()
    {
        var store = new PluginStore(Path_);
        var results = new ConcurrentBag<bool>();

        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            store.Set("count", i);
            results.Add(store.TrySave());
        });

        Assert.All(results, Assert.True);
        Assert.True(store.Writable);
        Assert.False(File.Exists(Path_ + ".tmp"));

        var read = new PluginStore(Path_);
        read.Load();

        Assert.InRange(read.Get<int>("count"), 0, 399);
    }

    [Fact]
    public void A_key_that_cannot_be_read_as_the_asked_type_warns_once()
    {
        var lines = new List<string>();
        var store = new PluginStore(Path_, (level, message) => lines.Add(level + " " + message));
        store.Set("stock", "not a number");

        Assert.Equal(0, store.Get<int>("stock"));
        Assert.Equal(0, store.Get<int>("stock"));

        string warn = Assert.Single(lines, line => line.StartsWith("WARN"));
        Assert.Contains("stock", warn);
        Assert.Contains(Path_, warn);
    }

    [Fact]
    public void A_readable_key_says_nothing()
    {
        var lines = new List<string>();
        var store = new PluginStore(Path_, (level, message) => lines.Add(level + " " + message));
        store.Set("stock", 12);

        Assert.Equal(12, store.Get<int>("stock"));
        Assert.Empty(lines);
    }
}
