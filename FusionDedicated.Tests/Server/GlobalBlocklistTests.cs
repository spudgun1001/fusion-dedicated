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
    public void A_barcode_only_the_global_list_knows_is_still_blocked()
    {
        var verdict = Evaluator(Global()).Check("SLZ.BONELAB.Core.Spawnable.RigManagerBlank");

        Assert.Equal("global", verdict.Layer);
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

/// <summary>
/// Getting one mod back when a list the operator did not write refuses it.
///
/// Fusion's global list is advice to a server owner, and the whitelist did not
/// cover it, so the only lever was turning the whole global list off for
/// everything. That matters more now the server learns mod.io ids from its own
/// players: the catalogue is what lets a mod id be matched at all, so as it
/// fills, the global list starts refusing barcodes it could not match before.
/// </summary>
public class GlobalWhitelistTests
{
    private static GlobalModBlacklist Global() => new()
    {
        Mods = { new GlobalModEntry { NameId = "gun-gun", ModId = 4457523 } },
    };

    private static BlocklistEvaluator Evaluator(BlocklistFile? file, IReadOnlyDictionary<string, int>? catalogue = null)
        => new(new HashSet<string>(StringComparer.Ordinal), Global(), catalogue, file);

    [Fact]
    public void A_globally_listed_barcode_is_still_refused_by_default()
    {
        var verdict = Evaluator(new BlocklistFile()).Check("gun-gun.Spawnable.Rifle");

        Assert.True(verdict.Blocked);
        Assert.Equal("global", verdict.Layer);
    }

    [Fact]
    public void Naming_it_in_the_whitelist_allows_it()
    {
        var file = new BlocklistFile();
        file.Whitelist.Add("gun-gun.Spawnable.Rifle");

        Assert.False(Evaluator(file).Check("gun-gun.Spawnable.Rifle").Blocked);
    }

    [Fact]
    public void The_rest_of_that_mod_is_still_refused()
    {
        // One barcode back, not the whole mod.
        var file = new BlocklistFile();
        file.Whitelist.Add("gun-gun.Spawnable.Rifle");

        Assert.True(Evaluator(file).Check("gun-gun.Spawnable.Launcher").Blocked);
    }

    [Fact]
    public void A_mod_matched_by_its_learned_mod_io_id_can_be_whitelisted_too()
    {
        // The catalogue is how a mod id is matched at all, and it now fills
        // itself from the players on the server.
        var catalogue = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["SomePack.Spawnable.Rifle"] = 4457523,
        };

        Assert.True(Evaluator(new BlocklistFile(), catalogue).Check("SomePack.Spawnable.Rifle").Blocked);

        var file = new BlocklistFile();
        file.Whitelist.Add("SomePack.Spawnable.Rifle");

        Assert.False(Evaluator(file, catalogue).Check("SomePack.Spawnable.Rifle").Blocked);
    }

    [Fact]
    public void A_disabled_blocklist_file_carries_no_whitelist_either()
    {
        // Turning the file off turns all of it off, the whitelist included, which
        // is what Enabled has always meant.
        var file = new BlocklistFile { Enabled = false };
        file.Whitelist.Add("gun-gun.Spawnable.Rifle");

        Assert.True(Evaluator(file).Check("gun-gun.Spawnable.Rifle").Blocked);
    }
}
