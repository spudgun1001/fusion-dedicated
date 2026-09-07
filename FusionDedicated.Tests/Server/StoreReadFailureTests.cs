using FusionDedicated.Server.Bans;
using FusionDedicated.Server.Props;
using FusionDedicated.Server.Ranks;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Reading a file somebody else is writing throws IOException, not JsonException.
///
/// Every one of these stores is reloaded from the main loop every ten seconds,
/// and the loop has nothing around it. So the panel saving a rank at the same
/// moment the reload ran threw out of the loop and ended the process. It showed
/// up as a test that failed about one run in three and passed alone, which is
/// exactly what a timing collision looks like from the outside.
/// </summary>
public class StoreReadFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-locked-" + Guid.NewGuid().ToString("N"));

    public StoreReadFailureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>A file nobody else may read, the way one mid-write is.</summary>
    private (string Path, FileStream Hold) Locked(string name)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, "{}");

        var hold = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);

        return (path, hold);
    }

    [Fact]
    public void Ranks_being_written_do_not_take_the_server_with_them()
    {
        var (path, hold) = Locked("ranks.json");
        using (hold)
        {
            var store = new RankStore(path);

            Assert.Null(Record.Exception(() => store.Load()));
        }
    }

    [Fact]
    public void Bans_being_written_do_not_take_the_server_with_them()
    {
        var (path, hold) = Locked("bans.json");
        using (hold)
        {
            var store = new BanStore(path);

            Assert.Null(Record.Exception(() => store.Load()));
        }
    }

    [Fact]
    public void A_blocklist_being_written_does_not_take_the_server_with_it()
    {
        var (path, hold) = Locked("blocklist.json");
        using (hold)
        {
            var store = new BlocklistStore(path);

            Assert.Null(Record.Exception(() => store.ReloadIfChanged()));
        }
    }

    [Fact]
    public void Props_being_written_do_not_take_the_server_with_them()
    {
        var (path, hold) = Locked("props.json");
        using (hold)
        {
            var store = new PersistentPropStore(path);

            Assert.Null(Record.Exception(() => store.Load()));
        }
    }

    [Fact]
    public void What_was_already_loaded_is_kept_when_the_read_fails()
    {
        // Losing every rank because the file was busy for a moment would be
        // worse than the crash it used to cause.
        string path = Path.Combine(_dir, "keep.json");

        var store = new RankStore(path);
        store.Set(76561198000000001, "Terminator", PermissionLevel.Owner);
        store.Save();

        using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            store.Load();
        }

        Assert.Equal(PermissionLevel.Owner, store.Get(76561198000000001));
    }

    [Fact]
    public void A_read_that_failed_is_tried_again_rather_than_lost()
    {
        // The stamp used to move before the read. Once the read was allowed to
        // fail quietly, that meant an edit made while the file was busy was
        // marked as seen and never looked at again: silently discarded until
        // somebody touched the file a second time.
        string path = Path.Combine(_dir, "retry.json");

        var writer = new RankStore(path);
        writer.Set(76561198000000001, "Terminator", PermissionLevel.Owner);
        writer.Save();

        var reader = new RankStore(path);
        reader.Load();

        // Somebody edits the file, and the read collides with a write.
        writer.Set(76561198000000002, "Kanzaaa", PermissionLevel.Operator);
        writer.Save();

        using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            Assert.False(reader.ReloadIfChanged());
        }

        // The file is free again, so the change is still waiting to be read.
        Assert.True(reader.ReloadIfChanged());
        Assert.Equal(PermissionLevel.Operator, reader.Get(76561198000000002));
    }

    [Fact]
    public void A_file_that_has_not_changed_is_not_read_twice()
    {
        string path = Path.Combine(_dir, "steady.json");

        var store = new RankStore(path);
        store.Set(76561198000000001, "Terminator", PermissionLevel.Owner);
        store.Save();

        store.ReloadIfChanged();

        Assert.False(store.ReloadIfChanged());
    }
}
