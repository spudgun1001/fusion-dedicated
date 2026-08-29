using FusionDedicated;
using FusionDedicated.Server.Ranks;

namespace FusionDedicated.Tests.Server;

public class RankStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-ranks-" + Guid.NewGuid());

    public RankStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string RanksFile => Path.Combine(_dir, "ranks.json");

    [Fact]
    public void Unlisted_player_is_default()
    {
        Assert.Equal(PermissionLevel.Default, new RankStore(RanksFile).Get(1));
    }

    [Fact]
    public void Set_then_get_round_trips_through_disk()
    {
        var store = new RankStore(RanksFile);
        store.Set(76561198000000000, "spudgun", PermissionLevel.Owner);
        store.Save();

        var reloaded = new RankStore(RanksFile);
        reloaded.Load();

        Assert.Equal(PermissionLevel.Owner, reloaded.Get(76561198000000000));
        Assert.Equal("spudgun", reloaded.Entries[76561198000000000].Name);
    }

    [Fact]
    public void Setting_default_removes_the_entry()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "x", PermissionLevel.Operator);
        store.Set(1, "x", PermissionLevel.Default);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void A_malformed_file_keeps_the_previous_roster()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "x", PermissionLevel.Owner);
        store.Save();
        store.Load();

        File.WriteAllText(RanksFile, "{ not json");
        store.Load();

        Assert.Equal(PermissionLevel.Owner, store.Get(1));
    }

    [Fact]
    public void Migration_copies_entries_and_skips_duplicates()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "already", PermissionLevel.Owner);

        int added = store.MigrateFrom(new[]
        {
            new PermissionEntry { PlatformId = 1, Username = "already", Level = PermissionLevel.Operator },
            new PermissionEntry { PlatformId = 2, Username = "new", Level = PermissionLevel.Operator },
        });

        Assert.Equal(1, added);
        Assert.Equal(PermissionLevel.Owner, store.Get(1));
        Assert.Equal(PermissionLevel.Operator, store.Get(2));
    }

    [Fact]
    public void Seeding_adds_without_removing_existing_entries()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "existing", PermissionLevel.Operator);

        int added = store.MergeSeed(new ulong[] { 2, 3 }, PermissionLevel.Owner);

        Assert.Equal(2, added);
        Assert.Equal(PermissionLevel.Operator, store.Get(1));
        Assert.Equal(PermissionLevel.Owner, store.Get(2));
    }

    [Fact]
    public void Seeding_does_not_downgrade_an_existing_rank()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "owner", PermissionLevel.Owner);

        store.MergeSeed(new ulong[] { 1 }, PermissionLevel.Operator);

        Assert.Equal(PermissionLevel.Owner, store.Get(1));
    }
}
