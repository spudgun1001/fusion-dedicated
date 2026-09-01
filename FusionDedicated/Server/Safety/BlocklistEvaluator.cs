namespace FusionDedicated.Server.Safety;

public sealed record BlockVerdict(bool Blocked, string Layer, string Reason)
{
    public static readonly BlockVerdict Allowed = new(false, "", "");
}

/// <summary>
/// Decides whether a barcode may be spawned. The owner's whitelist wins over their
/// own rules but not over Fusion's community list, which covers mods that are
/// malicious rather than merely unwanted.
/// </summary>
public sealed class BlocklistEvaluator
{
    private readonly IReadOnlySet<string> _operatorBarcodes;
    private readonly GlobalModBlacklist? _global;
    private readonly IReadOnlyDictionary<string, int> _catalogue;
    private readonly BlocklistFile? _file;

    public BlocklistEvaluator(
        IReadOnlySet<string> operatorBarcodes,
        GlobalModBlacklist? global = null,
        IReadOnlyDictionary<string, int>? catalogue = null,
        BlocklistFile? file = null)
    {
        _operatorBarcodes = operatorBarcodes;
        _global = global;
        _catalogue = catalogue ?? new Dictionary<string, int>();
        _file = file?.Enabled == true ? file : null;
    }

    public BlockVerdict Check(string barcode, PermissionLevel senderRank = PermissionLevel.Default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BlockVerdict.Allowed;
        }

        bool whitelisted = _file?.Whitelist.Contains(barcode, StringComparer.Ordinal) == true;

        if (!whitelisted && _file != null)
        {
            if (_file.Barcodes.Contains(barcode, StringComparer.Ordinal))
            {
                return new BlockVerdict(true, "blocklist", "on this server's blocklist");
            }

            foreach (string keyword in _file.Keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword)
                    && barcode.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return new BlockVerdict(true, "keyword", $"barcode contains '{keyword}'");
                }
            }
        }

        if (_global != null && MatchesGlobal(barcode, out string reason))
        {
            return new BlockVerdict(true, "global", reason);
        }

        if (!whitelisted && _operatorBarcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "operator", "on this server's blacklist");
        }

        if (_file != null
            && _file.OperatorOnly.Contains(barcode, StringComparer.Ordinal)
            && !senderRank.IsAtLeast(PermissionLevel.Operator))
        {
            return new BlockVerdict(true, "operator-only", "only operators may spawn this");
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
