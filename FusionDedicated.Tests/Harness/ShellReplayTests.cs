using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A shotgun eats each shell loaded into it and sends no eject, so every shell but the
/// last stayed on the books and was spawned for each joiner, stuck in the gun.
/// </summary>
public class ShellReplayTests
{
    private const ushort Shotgun = 300;

    private static (World World, FakePlayer Joel) Build()
    {
        var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, Shotgun, "Pack.Spawnable.Mossberg", 0, 0, 0);

        return (world, joel);
    }

    private static FakePlayer Joiner(World world)
    {
        var late = world.Join(76561198000000002, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        return late;
    }

    private static List<ushort> MagazinesInsertedFor(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection)
            .Where(sent => ModuleProtocol.TryReadHandlerTag(sent.Message) == ModuleProtocol.MagazineInsertTag)
            .Select(sent => ModuleProtocol.ReadAttachment(ModuleProtocol.MagazineInsertTag,
                ModuleProtocol.TryReadHandlerPayload(sent.Message)).Entity)
            .ToList();

    [Fact]
    public void A_shotgun_loaded_with_three_shells_is_replayed_to_a_joiner_with_at_most_the_last()
    {
        var (world, joel) = Build();
        using var _ = world;

        foreach (ushort shell in new ushort[] { 301, 302, 303 })
        {
            world.Spawn(joel, shell, "Pack.Spawnable.Mag12Gauge", 0, 0, 0);
            joel.Send(ClientMessages.MagazineInsert(joel.SmallId, shell, Shotgun));
        }

        var late = Joiner(world);

        Assert.False(late.View.Entities.ContainsKey(301), "a consumed shell was spawned for the joiner");
        Assert.False(late.View.Entities.ContainsKey(302), "a consumed shell was spawned for the joiner");
        Assert.All(MagazinesInsertedFor(world, late), magazine => Assert.Equal((ushort)303, magazine));
        Assert.Null(world.Server.Entities.Get(301));
        Assert.Null(world.Server.Entities.Get(302));
    }

    [Fact]
    public void A_pistol_magazine_is_still_replayed_after_an_eject_and_reload()
    {
        var (world, joel) = Build();
        using var _ = world;
        world.Spawn(joel, 301, "Pack.Spawnable.MagPistol", 0, 0, 0);
        world.Spawn(joel, 302, "Pack.Spawnable.MagPistol", 0, 0, 0);

        joel.Send(ClientMessages.MagazineInsert(joel.SmallId, 301, Shotgun));
        joel.Send(ClientMessages.MagazineEject(joel.SmallId, 301, Shotgun));
        joel.Send(ClientMessages.MagazineInsert(joel.SmallId, 302, Shotgun));

        var late = Joiner(world);

        Assert.True(late.View.Entities.ContainsKey(301), "the ejected magazine is gone");
        Assert.Contains((ushort)302, MagazinesInsertedFor(world, late));
    }
}
