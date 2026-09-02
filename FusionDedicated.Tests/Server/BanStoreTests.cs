using FusionDedicated;
using FusionDedicated.Server.Bans;

namespace FusionDedicated.Tests.Server;

public class BanStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-bans-" + Guid.NewGuid());

    public BanStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string BansFile => Path.Combine(_dir, "bans.json");

    [Fact]
    public void An_unlisted_player_is_not_banned()
    {
        Assert.False(new BanStore(BansFile).IsBanned(1));
    }

    [Fact]
    public void A_ban_round_trips_through_disk()
    {
        var store = new BanStore(BansFile);
        store.Ban(76561198000000000, "griefer", "spawning nukes");
        store.Save();

        var reloaded = new BanStore(BansFile);
        reloaded.Load();

        Assert.True(reloaded.IsBanned(76561198000000000));
        Assert.Equal("spawning nukes", reloaded.Find(76561198000000000)!.Reason);
        Assert.Equal("griefer", reloaded.Find(76561198000000000)!.Name);
    }

    [Fact]
    public void Unban_removes_the_entry_and_reports_it()
    {
        var store = new BanStore(BansFile);
        store.Ban(1, "x", "reason");

        Assert.True(store.Unban(1));
        Assert.False(store.IsBanned(1));
        Assert.False(store.Unban(1));
    }

    [Fact]
    public void Banning_twice_updates_rather_than_duplicates()
    {
        var store = new BanStore(BansFile);
        store.Ban(1, "x", "first");
        store.Ban(1, "x", "second");

        Assert.Single(store.Entries);
        Assert.Equal("second", store.Find(1)!.Reason);
    }

    [Fact]
    public void A_malformed_file_keeps_the_previous_list()
    {
        var store = new BanStore(BansFile);
        store.Ban(1, "x", "reason");
        store.Save();
        store.Load();

        File.WriteAllText(BansFile, "{ not json");
        store.Load();

        Assert.True(store.IsBanned(1));
    }

    [Fact]
    public void An_edit_on_disk_is_picked_up_without_a_restart()
    {
        var store = new BanStore(BansFile);
        store.Ban(1, "x", "reason");
        store.Save();
        store.ReloadIfChanged();

        File.WriteAllText(BansFile,
            """{"1":{"name":"x","reason":"reason"},"2":{"name":"y","reason":"added by hand"}}""");
        File.SetLastWriteTimeUtc(BansFile, DateTime.UtcNow.AddSeconds(5));

        Assert.True(store.ReloadIfChanged());
        Assert.True(store.IsBanned(2));
    }

    [Fact]
    public void Migration_copies_config_bans_and_skips_duplicates()
    {
        var store = new BanStore(BansFile);
        store.Ban(1, "already", "kept");

        int added = store.MigrateFrom(new[]
        {
            new BanEntry { PlatformId = 1, Username = "already", Reason = "overwritten?" },
            new BanEntry { PlatformId = 2, Username = "new", Reason = "imported" },
        });

        Assert.Equal(1, added);
        Assert.Equal("kept", store.Find(1)!.Reason);
        Assert.Equal("imported", store.Find(2)!.Reason);
    }

    [Fact]
    public void Entries_are_listed_for_the_panel()
    {
        var store = new BanStore(BansFile);
        store.Ban(1, "a", "one");
        store.Ban(2, "b", "two");

        Assert.Equal(2, store.Entries.Count);
    }
}
