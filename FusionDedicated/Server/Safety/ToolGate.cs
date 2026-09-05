namespace FusionDedicated.Server.Safety;

public enum ToolFamily
{
    None = 0,
    DevTools = 1,
    Constrainer = 2,
    Nimbus = 3,
}

/// <summary>The minimum level each tool family needs, taken from the server config.</summary>
public readonly record struct ToolGates(
    PermissionLevel DevTools,
    PermissionLevel Constrainer,
    PermissionLevel Nimbus);

/// <summary>
/// Sorts a barcode into a tool family and says whether a player of a given rank may
/// have it. Clients gate their own menus on these settings, so without a server side
/// check a modified client picks up whatever it likes.
///
/// Matching is on the readable tail of the barcode rather than a fixed list, because
/// the same tool arrives under a different barcode from every mod that repackages it.
/// </summary>
public static class ToolGate
{
    /// <summary>
    /// The base game's dev tool crates. Their barcodes carry no readable name, so
    /// they have to be listed. Taken from the shipped pallet manifests for
    /// SLZ.BONELAB.Core and SLZ.BONELAB.Content.
    /// </summary>
    private static readonly Dictionary<string, ToolFamily> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c1534c5a-6b38-438a-a324-d7e147616467"] = ToolFamily.Nimbus,        // Nimbus Gun
        ["c1534c5a-3813-49d6-a98c-f595436f6e73"] = ToolFamily.Constrainer,   // Constrainer
        ["c1534c5a-5747-42a2-bd08-ab3b47616467"] = ToolFamily.DevTools,      // Spawn Gun
        ["c1534c5a-c6a8-45d0-aaa2-2c954465764d"] = ToolFamily.DevTools,      // Dev Manipulator
        ["c1534c5a-e777-4d15-b0c1-3195426f6172"] = ToolFamily.DevTools,      // Boardgun
    };

    // A repackaged tool arrives under the mod's own barcode, which does carry a
    // readable name. Longest and most specific first.
    private static readonly (string Keyword, ToolFamily Family)[] Names =
    {
        ("nimbusgun", ToolFamily.Nimbus),
        ("nimbus_gun", ToolFamily.Nimbus),
        ("nimbus", ToolFamily.Nimbus),
        ("constrainer", ToolFamily.Constrainer),
        ("constraincer", ToolFamily.Constrainer),
        ("spawngun", ToolFamily.DevTools),
        ("spawn_gun", ToolFamily.DevTools),
        ("toolgun", ToolFamily.DevTools),
        ("tool_gun", ToolFamily.DevTools),
        ("devtool", ToolFamily.DevTools),
        ("dev_tool", ToolFamily.DevTools),
    };

    public static ToolFamily Family(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return ToolFamily.None;
        }

        if (Known.TryGetValue(barcode.Trim(), out var known))
        {
            return known;
        }

        foreach (var (keyword, family) in Names)
        {
            if (barcode.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return family;
            }
        }

        return ToolFamily.None;
    }

    /// <summary>
    /// The nimbus gun is a dev tool as far as the base game is concerned, and Fusion
    /// has no permission of its own for it, so restricting dev tools restricts it.
    /// Its own setting is there to go stricter still, never looser.
    /// </summary>
    public static PermissionLevel Required(ToolFamily family, ToolGates gates) => family switch
    {
        ToolFamily.DevTools => gates.DevTools,
        ToolFamily.Constrainer => gates.Constrainer,
        ToolFamily.Nimbus => Stricter(gates.Nimbus, gates.DevTools),
        _ => PermissionLevel.Guest,
    };

    private static PermissionLevel Stricter(PermissionLevel a, PermissionLevel b)
        => a > b ? a : b;

    public static BlockVerdict Check(string barcode, PermissionLevel rank, ToolGates gates)
    {
        var family = Family(barcode);

        if (family == ToolFamily.None)
        {
            return BlockVerdict.Allowed;
        }

        var required = Required(family, gates);

        return rank.IsAtLeast(required)
            ? BlockVerdict.Allowed
            : new BlockVerdict(true, "tool-gate",
                $"{family} needs {required.ToFusionString()}");
    }
}

/// <summary>
/// Who may spawn anything at all. The tool gate only knows dev tools and the
/// blocklist only knows barcodes it was told about, so a spawn menu mod could
/// otherwise hand a weapon to anybody. Every spawn path has to ask the server for
/// an entity id, so this covers the ones nobody has thought of yet.
/// </summary>
public static class SpawnAuthority
{
    public static BlockVerdict Check(PermissionLevel rank, PermissionLevel required)
        => Check(rank, required, "", Array.Empty<string>());

    /// <summary>
    /// A gun asking for a magazine comes down the same path as a spawn menu, so
    /// refusing it by rank would stop people reloading. Anything named in
    /// <paramref name="exempt"/> passes whatever the spawner's rank, matched either
    /// as a whole barcode or as a word inside one.
    /// </summary>
    public static BlockVerdict Check(
        PermissionLevel rank, PermissionLevel required,
        string barcode, IEnumerable<string> exempt)
    {
        if (rank.IsAtLeast(required))
        {
            return BlockVerdict.Allowed;
        }

        if (IsExempt(barcode, exempt))
        {
            return BlockVerdict.Allowed;
        }

        return new BlockVerdict(true, "rank", $"spawning needs {required.ToFusionString()}");
    }

    public static bool IsExempt(string barcode, IEnumerable<string> exempt)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        foreach (string entry in exempt)
        {
            // A blank line in the config would otherwise exempt everything.
            if (!string.IsNullOrWhiteSpace(entry)
                && barcode.Contains(entry.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
