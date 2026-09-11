using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// What the cull counts as in use. A held or holstered prop sends no poses, so it
/// looked abandoned and went while somebody was carrying it.
/// </summary>
public class InUseTests
{
    private static readonly ushort[] Nothing = Array.Empty<ushort>();
    private static readonly (ushort Magazine, ushort Gun)[] NoMagazines = Array.Empty<(ushort, ushort)>();

    [Fact]
    public void A_held_prop_is_in_use()
        => Assert.Contains((ushort)300, InUse.Of(new ushort[] { 300 }, Nothing, NoMagazines));

    [Fact]
    public void A_holstered_prop_is_in_use()
        => Assert.Contains((ushort)300, InUse.Of(Nothing, new ushort[] { 300 }, NoMagazines));

    [Fact]
    public void A_magazine_in_a_held_gun_is_in_use()
        => Assert.Contains((ushort)301,
            InUse.Of(new ushort[] { 300 }, Nothing, new (ushort, ushort)[] { (301, 300) }));

    [Fact]
    public void A_magazine_in_a_holstered_gun_is_in_use()
        => Assert.Contains((ushort)301,
            InUse.Of(Nothing, new ushort[] { 300 }, new (ushort, ushort)[] { (301, 300) }));

    [Fact]
    public void A_magazine_in_a_gun_nobody_has_is_not_in_use()
        => Assert.Empty(InUse.Of(Nothing, Nothing, new (ushort, ushort)[] { (301, 300) }));
}
