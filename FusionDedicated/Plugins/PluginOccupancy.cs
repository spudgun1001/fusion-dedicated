namespace FusionDedicated.Plugins;

/// <summary>
/// Turns raw grab holders into what a plugin sees: platform ids only, and only for
/// somebody still connected and close enough to be believed.
/// </summary>
public static class PluginOccupancy
{
    public static IReadOnlyList<ulong> Holders(
        IReadOnlyList<byte> smallIds,
        Func<byte, ulong?> platformOf,
        Func<byte, (float X, float Y, float Z)?> positionOf,
        (float X, float Y, float Z)? entityAt,
        float reach = 5f)
    {
        var holders = new List<ulong>();

        foreach (byte smallId in smallIds)
        {
            if (platformOf(smallId) is not { } platformId)
            {
                // Gone. Whoever was last seen holding it is not holding it now.
                continue;
            }

            if (entityAt is { } at && positionOf(smallId) is { } position && TooFar(position, at, reach))
            {
                continue;
            }

            holders.Add(platformId);
        }

        return holders;
    }

    private static bool TooFar((float X, float Y, float Z) position, (float X, float Y, float Z) at, float reach)
    {
        float dx = position.X - at.X;
        float dy = position.Y - at.Y;
        float dz = position.Z - at.Z;

        return dx * dx + dy * dy + dz * dz > reach * reach;
    }
}
