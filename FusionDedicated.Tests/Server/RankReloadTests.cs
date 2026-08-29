using FusionDedicated;
using FusionDedicated.Server.Ranks;

namespace FusionDedicated.Tests.Server;

public class RankReloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-reload-" + Guid.NewGuid());

    public RankReloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string RanksFile => Path.Combine(_dir, "ranks.json");

    [Fact]
    public void ReloadIfChanged_is_false_when_nothing_changed()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "x", PermissionLevel.Owner);
        store.Save();
        store.ReloadIfChanged();

        Assert.False(store.ReloadIfChanged());
    }

    [Fact]
    public void ReloadIfChanged_picks_up_an_external_edit()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "x", PermissionLevel.Operator);
        store.Save();
        store.ReloadIfChanged();

        File.WriteAllText(RanksFile,
            """{ "1": { "rank": "Owner", "name": "x" }, "2": { "rank": "Operator", "name": "y" } }""");
        File.SetLastWriteTimeUtc(RanksFile, DateTime.UtcNow.AddSeconds(5));

        Assert.True(store.ReloadIfChanged());
        Assert.Equal(PermissionLevel.Owner, store.Get(1));
        Assert.Equal(PermissionLevel.Operator, store.Get(2));
    }

    [Fact]
    public void A_malformed_external_edit_keeps_the_previous_roster()
    {
        var store = new RankStore(RanksFile);
        store.Set(1, "x", PermissionLevel.Owner);
        store.Save();
        store.ReloadIfChanged();

        File.WriteAllText(RanksFile, "{ not json");
        File.SetLastWriteTimeUtc(RanksFile, DateTime.UtcNow.AddSeconds(5));

        store.ReloadIfChanged();

        Assert.Equal(PermissionLevel.Owner, store.Get(1));
    }

    [Fact]
    public void ReloadIfChanged_is_false_when_the_file_does_not_exist()
    {
        Assert.False(new RankStore(RanksFile).ReloadIfChanged());
    }
}
