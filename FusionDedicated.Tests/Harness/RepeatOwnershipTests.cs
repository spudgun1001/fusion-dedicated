using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A client asks again for what it already owns on every impact. Each copy went to every
/// player, so one car ridden and bumped came to about 20,000 reliable sends a second.
/// </summary>
public class RepeatOwnershipTests
{
    private const ushort Crate = 300;

    private static int OwnershipResponsesTo(World world, FakePlayer player, ushort entity, int from = 0)
        => world.Transport.SentTo(player.Connection)
            .Skip(from)
            .Count(sent => FusionProtocol.TryReadOwnershipResponse(sent.Message) is { } response
                && response.EntityId == entity);

    private static int GivenLines(World world, ushort entity)
        => world.Server.RecentLog(2000).Count(e => e.Message.StartsWith($"Ownership of entity {entity} given"));

    private static (World World, FakePlayer Joel, FakePlayer Kanza) CrateOwnedByJoel()
    {
        var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);

        return (world, joel, kanza);
    }

    [Fact]
    public void The_owner_asking_again_is_sent_to_nobody_else_and_not_logged()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;
        int before = world.Transport.SentTo(kanza.Connection).Count;

        for (var i = 0; i < 20; i++)
        {
            joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));
        }

        Assert.Equal(0, OwnershipResponsesTo(world, kanza, Crate, before));
        Assert.Equal(0, GivenLines(world, Crate));
        Assert.Equal(joel.SmallId, world.Server.Entities.Get(Crate)!.OwnerSmallId);
    }

    [Fact]
    public void The_owner_asking_again_is_confirmed_once_then_again_after_half_a_second()
    {
        var (world, joel, _) = CrateOwnedByJoel();
        using var __ = world;
        int before = world.Transport.SentTo(joel.Connection).Count;

        for (var i = 0; i < 20; i++)
        {
            joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));
        }

        Assert.Equal(1, OwnershipResponsesTo(world, joel, Crate, before));

        world.Advance(TimeSpan.FromMilliseconds(499));
        joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));

        Assert.Equal(1, OwnershipResponsesTo(world, joel, Crate, before));

        world.Advance(TimeSpan.FromMilliseconds(1));
        joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));

        Assert.Equal(2, OwnershipResponsesTo(world, joel, Crate, before));
    }

    [Fact]
    public void A_confirmation_names_the_owner_who_asked()
    {
        var (world, joel, _) = CrateOwnedByJoel();
        using var __ = world;
        int before = world.Transport.SentTo(joel.Connection).Count;

        joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));

        var confirmation = world.Transport.SentTo(joel.Connection).Skip(before)
            .Select(sent => (Response: FusionProtocol.TryReadOwnershipResponse(sent.Message), sent.Reliable))
            .Single(s => s.Response is { } r && r.EntityId == Crate);

        Assert.Equal(joel.SmallId, confirmation.Response!.Value.PlayerId);
        Assert.True(confirmation.Reliable);
    }

    [Fact]
    public void The_owner_asking_again_is_not_put_to_plugins()
    {
        var (world, joel, _) = CrateOwnedByJoel();
        using var __ = world;
        int raised = 0;
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Ownership.Subscribe("counter", _ =>
        {
            raised++;
            return PluginVerdict.Allow;
        });
        world.Server.Plugins = events;

        for (var i = 0; i < 5; i++)
        {
            joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));
        }

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_real_change_still_goes_to_everybody_and_is_logged()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;
        var dennis = world.Join(76561198000000003, "Dennis");
        dennis.FinishLoading();
        int joelBefore = world.Transport.SentTo(joel.Connection).Count;
        int dennisBefore = world.Transport.SentTo(dennis.Connection).Count;

        kanza.Send(FusionProtocol.BuildOwnershipRequest(kanza.SmallId, Crate));

        Assert.Equal(kanza.SmallId, world.Server.Entities.Get(Crate)!.OwnerSmallId);
        Assert.Equal(1, OwnershipResponsesTo(world, joel, Crate, joelBefore));
        Assert.Equal(1, OwnershipResponsesTo(world, dennis, Crate, dennisBefore));
        Assert.Equal(1, GivenLines(world, Crate));
        Assert.True(world.AgreeOnOwner(Crate), string.Join(", ", world.OwnersOf(Crate)));
    }

    [Fact]
    public void Taking_it_back_after_losing_it_goes_to_everybody_again()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));
        kanza.Send(FusionProtocol.BuildOwnershipRequest(kanza.SmallId, Crate));
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(FusionProtocol.BuildOwnershipRequest(joel.SmallId, Crate));

        Assert.Equal(joel.SmallId, world.Server.Entities.Get(Crate)!.OwnerSmallId);
        Assert.Equal(1, OwnershipResponsesTo(world, kanza, Crate, before));
        Assert.True(world.AgreeOnOwner(Crate), string.Join(", ", world.OwnersOf(Crate)));
    }
}
