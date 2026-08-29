using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class GlobalBlocklistTests
{
    private static GlobalModBlacklist Global() => new()
    {
        Mods =
        {
            new GlobalModEntry { NameId = "gun-gun", ModId = 4457523 },
            new GlobalModEntry
            {
                NameId = "bonelab",
                ModId = -1,
                Barcodes = { "SLZ.BONELAB.Core.Spawnable.RigManagerBlank" },
            },
        },
    };

    private static BlocklistEvaluator Evaluator(
        GlobalModBlacklist? global = null,
        IReadOnlyDictionary<string, int>? catalogue = null)
        => new(new HashSet<string>(StringComparer.Ordinal), global, catalogue);

    [Fact]
    public void Global_barcode_is_blocked()
    {
        var verdict = Evaluator(Global()).Check("SLZ.BONELAB.Core.Spawnable.RigManagerBlank");

        Assert.True(verdict.Blocked);
        Assert.Equal("global", verdict.Layer);
    }

    [Fact]
    public void Name_id_matches_the_pallet_portion_of_a_barcode()
    {
        var verdict = Evaluator(Global()).Check("gun-gun.SomePallet.Spawnable.Thing");

        Assert.True(verdict.Blocked);
        Assert.Equal("global", verdict.Layer);
    }

    [Fact]
    public void Mod_id_blocks_only_a_catalogued_barcode()
    {
        var catalogue = new Dictionary<string, int> { ["Author.Pallet.Spawnable.X"] = 4457523 };

        Assert.True(Evaluator(Global(), catalogue).Check("Author.Pallet.Spawnable.X").Blocked);
        Assert.False(Evaluator(Global()).Check("Author.Pallet.Spawnable.X").Blocked);
    }

    [Fact]
    public void A_null_global_list_blocks_nothing_extra()
    {
        Assert.False(Evaluator().Check("SLZ.BONELAB.Core.Spawnable.RigManagerBlank").Blocked);
    }

    [Fact]
    public void Built_in_still_wins_when_a_global_list_is_present()
    {
        var verdict = Evaluator(Global()).Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke");

        Assert.Equal("built-in", verdict.Layer);
    }
}

public class SafetyListStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-tests-" + Guid.NewGuid());

    public SafetyListStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Good = """{ "mods": [ { "barcodes": ["A.B.C.D"], "modID": 1, "nameID": "x" } ] }""";

    [Fact]
    public async Task Refresh_stores_a_good_download_in_the_cache()
    {
        var store = new SafetyListStore(_dir);

        await store.RefreshAsync(_ => Task.FromResult<string?>(Good));

        Assert.NotNull(store.Mods);
        Assert.True(File.Exists(Path.Combine(_dir, "globalModBlacklist.json")));
    }

    [Fact]
    public async Task A_failed_download_keeps_the_previous_cache()
    {
        var store = new SafetyListStore(_dir);
        await store.RefreshAsync(_ => Task.FromResult<string?>(Good));

        await store.RefreshAsync(_ => Task.FromResult<string?>(null));

        Assert.NotNull(store.Mods);
        Assert.Single(store.Mods!.Mods);
    }

    [Fact]
    public async Task A_malformed_download_keeps_the_previous_cache()
    {
        var store = new SafetyListStore(_dir);
        await store.RefreshAsync(_ => Task.FromResult<string?>(Good));

        await store.RefreshAsync(_ => Task.FromResult<string?>("not json"));

        Assert.Single(store.Mods!.Mods);
    }

    [Fact]
    public void LoadCache_reads_what_a_previous_run_saved()
    {
        File.WriteAllText(Path.Combine(_dir, "globalModBlacklist.json"), Good);

        var store = new SafetyListStore(_dir);
        store.LoadCache();

        Assert.Single(store.Mods!.Mods);
    }

    [Fact]
    public void LoadCache_with_no_cache_leaves_the_lists_null()
    {
        var store = new SafetyListStore(_dir);
        store.LoadCache();

        Assert.Null(store.Mods);
    }
}
