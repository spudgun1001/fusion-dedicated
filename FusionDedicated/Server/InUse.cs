namespace FusionDedicated.Server;

/// <summary>
/// Props somebody is using, which no clock may remove. A held or holstered prop
/// sends no poses, so it looks abandoned however much it is wanted.
/// </summary>
public static class InUse
{
    /// <summary>Everything held or holstered, and any magazine in a gun that is.</summary>
    public static HashSet<ushort> Of(
        IEnumerable<ushort> held, IEnumerable<ushort> holstered, IEnumerable<(ushort Magazine, ushort Gun)> loaded)
    {
        var inUse = new HashSet<ushort>(held);
        inUse.UnionWith(holstered);

        foreach (var (magazine, gun) in loaded.ToList())
        {
            if (inUse.Contains(gun))
            {
                inUse.Add(magazine);
            }
        }

        return inUse;
    }
}
