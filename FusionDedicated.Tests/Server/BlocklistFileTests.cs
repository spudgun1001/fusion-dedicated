using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class BlocklistFileTests
{
    private static BlocklistEvaluator Evaluator(BlocklistFile? file)
        => new(new HashSet<string>(StringComparer.Ordinal), null, null, file);

    private static BlocklistFile Standard() => new()
    {
        Barcodes = { "BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke" },
        Keywords = { "nuke", "icbm" },
        Whitelist = { "Friendly.Pallet.Spawnable.NukeCola" },
        OperatorOnly = { "SLZ.BONELAB.Spawnable.Crablet" },
    };

    [Fact]
    public void A_listed_barcode_is_blocked()
    {
        var v = Evaluator(Standard()).Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke", PermissionLevel.Default);

        Assert.True(v.Blocked);
        Assert.Equal("blocklist", v.Layer);
    }

    [Fact]
    public void A_keyword_catches_an_unlisted_variant()
    {
        var v = Evaluator(Standard()).Check("Someone.Pack.Spawnable.BigNukeMk2", PermissionLevel.Default);

        Assert.True(v.Blocked);
        Assert.Equal("keyword", v.Layer);
        Assert.Contains("nuke", v.Reason);
    }

    [Fact]
    public void Keyword_matching_ignores_case()
    {
        Assert.True(Evaluator(Standard()).Check("A.B.Spawnable.ICBM", PermissionLevel.Default).Blocked);
    }

    [Fact]
    public void The_whitelist_overrides_barcodes_and_keywords()
    {
        var v = Evaluator(Standard()).Check("Friendly.Pallet.Spawnable.NukeCola", PermissionLevel.Default);

        Assert.False(v.Blocked);
    }

    [Fact]
    public void Operator_only_blocks_a_default_player()
    {
        var v = Evaluator(Standard()).Check("SLZ.BONELAB.Spawnable.Crablet", PermissionLevel.Default);

        Assert.True(v.Blocked);
        Assert.Equal("operator-only", v.Layer);
    }

    [Theory]
    [InlineData(PermissionLevel.Operator)]
    [InlineData(PermissionLevel.Owner)]
    public void Operator_only_allows_a_ranked_player(PermissionLevel rank)
    {
        Assert.False(Evaluator(Standard()).Check("SLZ.BONELAB.Spawnable.Crablet", rank).Blocked);
    }

    [Fact]
    public void A_missing_file_blocks_nothing()
    {
        Assert.False(Evaluator(null).Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke", PermissionLevel.Default).Blocked);
    }

    [Fact]
    public void A_disabled_file_blocks_nothing()
    {
        var file = Standard();
        file.Enabled = false;

        Assert.False(Evaluator(file).Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke", PermissionLevel.Default).Blocked);
    }

    [Fact]
    public void An_empty_keyword_never_matches_everything()
    {
        var file = new BlocklistFile { Keywords = { "" } };

        Assert.False(Evaluator(file).Check("Anything.At.All.Here", PermissionLevel.Default).Blocked);
    }
}

public class BlocklistStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-blocklist-" + Guid.NewGuid());

    public BlocklistStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "blocklist.json");

    [Fact]
    public void An_absent_file_loads_as_null_so_owners_can_delete_it()
    {
        var store = new BlocklistStore(FilePath);
        store.Load();

        Assert.Null(store.Current);
    }

    [Fact]
    public void A_valid_file_loads()
    {
        File.WriteAllText(FilePath,
            """{ "enabled": true, "barcodes": ["A.B.C.D"], "keywords": ["nuke"] }""");

        var store = new BlocklistStore(FilePath);
        store.Load();

        Assert.NotNull(store.Current);
        Assert.Contains("A.B.C.D", store.Current!.Barcodes);
        Assert.Contains("nuke", store.Current.Keywords);
    }

    [Fact]
    public void A_malformed_file_keeps_the_previous_list()
    {
        File.WriteAllText(FilePath, """{ "barcodes": ["A.B.C.D"] }""");

        var store = new BlocklistStore(FilePath);
        store.Load();

        File.WriteAllText(FilePath, "{ not json");
        store.Load();

        Assert.Single(store.Current!.Barcodes);
    }

    [Fact]
    public void An_edit_is_picked_up_without_a_restart()
    {
        File.WriteAllText(FilePath, """{ "barcodes": ["A.B.C.D"] }""");

        var store = new BlocklistStore(FilePath);
        store.ReloadIfChanged();

        File.WriteAllText(FilePath, """{ "barcodes": ["A.B.C.D", "E.F.G.H"] }""");
        File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow.AddSeconds(5));

        Assert.True(store.ReloadIfChanged());
        Assert.Equal(2, store.Current!.Barcodes.Count);
    }

    [Fact]
    public void Deleting_the_file_disables_the_layer_on_reload()
    {
        File.WriteAllText(FilePath, """{ "barcodes": ["A.B.C.D"] }""");

        var store = new BlocklistStore(FilePath);
        store.Load();
        Assert.NotNull(store.Current);

        File.Delete(FilePath);
        store.Load();

        Assert.Null(store.Current);
    }
}
