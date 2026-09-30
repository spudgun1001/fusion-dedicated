using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Fusion answers a joiner's catch-up for a magazine with a claim that loads it with
/// whatever cartridge the owner's rig last picked, so a pistol showed shotgun shells.
/// </summary>
public class MagazineClaimRelayTests
{
    private const ushort Magazine = 300;

    private static (World World, FakePlayer Joel, FakePlayer Late) Build(bool relayCatchupClaims)
    {
        var world = new World(new ServerConfig
        {
            CullOrphanedEntities = false,
            RelayCatchupMagazineClaims = relayCatchupClaims,
        });

        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, Magazine, "Pack.Spawnable.MagPistol", 0, 0, 0);

        var late = world.Join(76561198000000002, "Late");
        late.FinishLoading();

        return (world, joel, late);
    }

    private static int ClaimsSentTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection)
            .Count(sent => ModuleProtocol.TryReadHandlerTag(sent.Message) == ModuleProtocol.MagazineClaimTag);

    [Fact]
    public void A_catchup_magazine_claim_is_not_passed_on_but_a_live_one_is()
    {
        var (world, joel, late) = Build(relayCatchupClaims: false);
        using var _ = world;

        joel.Send(ClientMessages.MagazineClaim(joel.SmallId, Magazine, hand: 0, target: late.SmallId));

        Assert.Equal(0, ClaimsSentTo(world, late));

        joel.Send(ClientMessages.MagazineClaim(joel.SmallId, Magazine, hand: (byte)FusionProtocol.Handedness.RIGHT));

        Assert.Equal(1, ClaimsSentTo(world, late));
    }

    [Fact]
    public void Dropped_catchup_claims_are_logged_as_one_count()
    {
        var (world, joel, late) = Build(relayCatchupClaims: false);
        using var _ = world;

        joel.Send(ClientMessages.MagazineClaim(joel.SmallId, Magazine, hand: 0, target: late.SmallId));
        joel.Send(ClientMessages.MagazineClaim(joel.SmallId, Magazine, hand: 0, target: late.SmallId));
        world.Tick();

        Assert.Single(world.Server.RecentLog(2000), e => e.Message.Contains("catch-up magazine claim"));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Dropped 2 catch-up magazine claim(s) in the last minute");
    }

    [Fact]
    public void Catchup_magazine_claims_are_passed_on_when_the_setting_asks_for_it()
    {
        var (world, joel, late) = Build(relayCatchupClaims: true);
        using var _ = world;

        joel.Send(ClientMessages.MagazineClaim(joel.SmallId, Magazine, hand: 0, target: late.SmallId));

        Assert.Equal(1, ClaimsSentTo(world, late));
    }
}
