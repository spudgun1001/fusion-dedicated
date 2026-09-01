namespace FusionDedicated.Server.Safety;

public sealed record BlockVerdict(bool Blocked, string Layer, string Reason)
{
    public static readonly BlockVerdict Allowed = new(false, "", "");
}

/// <summary>
/// Decides whether a barcode may be spawned. Built-in is checked first so a
/// permissive operator list cannot re-enable a known grief payload.
/// </summary>
public sealed class BlocklistEvaluator
{
    private readonly IReadOnlySet<string> _operatorBarcodes;
    private readonly GlobalModBlacklist? _global;
    private readonly IReadOnlyDictionary<string, int> _catalogue;

    public BlocklistEvaluator(
        IReadOnlySet<string> operatorBarcodes,
        GlobalModBlacklist? global = null,
        IReadOnlyDictionary<string, int>? catalogue = null)
    {
        _operatorBarcodes = operatorBarcodes;
        _global = global;
        _catalogue = catalogue ?? new Dictionary<string, int>();
    }

    public BlockVerdict Check(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BlockVerdict.Allowed;
        }

        if (BuiltInBlocklist.Barcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "built-in", "known grief payload");
        }

        if (_global != null && MatchesGlobal(barcode, out string reason))
        {
            return new BlockVerdict(true, "global", reason);
        }

        if (_operatorBarcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "operator", "on this server's blacklist");
        }

        return BlockVerdict.Allowed;
    }

    /// <summary>
    /// A barcode's first segment is the name id Fusion's list uses. Mod id matching
    /// needs the catalogue, since a spawn request carries no mod id.
    /// </summary>
    private bool MatchesGlobal(string barcode, out string reason)
    {
        string nameId = barcode.Split('.', 2)[0];
        _catalogue.TryGetValue(barcode, out int modId);

        foreach (var mod in _global!.Mods)
        {
            if (mod.Barcodes.Contains(barcode, StringComparer.Ordinal))
            {
                reason = $"barcode listed under '{mod.NameId}'";
                return true;
            }

            if (!string.IsNullOrEmpty(mod.NameId)
                && string.Equals(mod.NameId, nameId, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"mod '{mod.NameId}' is blacklisted";
                return true;
            }

            if (mod.ModId > 0 && modId == mod.ModId)
            {
                reason = $"mod.io id {mod.ModId} is blacklisted";
                return true;
            }
        }

        reason = "";
        return false;
    }
}
