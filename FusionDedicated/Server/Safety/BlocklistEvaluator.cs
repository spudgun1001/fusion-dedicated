namespace FusionDedicated.Server.Safety;

public sealed record BlockVerdict(bool Blocked, string Layer, string Reason)
{
    public static readonly BlockVerdict Allowed = new(false, "", "");
}

/// <summary>
/// Decides whether a barcode may be spawned. Layers are checked built-in first so a
/// permissive operator list can never re-enable a known grief payload.
/// </summary>
public sealed class BlocklistEvaluator
{
    private readonly IReadOnlySet<string> _operatorBarcodes;

    public BlocklistEvaluator(IReadOnlySet<string> operatorBarcodes)
    {
        _operatorBarcodes = operatorBarcodes;
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

        if (_operatorBarcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "operator", "on this server's blacklist");
        }

        return BlockVerdict.Allowed;
    }
}
