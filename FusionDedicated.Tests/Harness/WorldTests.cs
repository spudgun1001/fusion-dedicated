using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

public class WorldTests
{
    [Fact]
    public void A_flooder_kicked_by_the_refusal_guard_is_dropped_from_the_world()
    {
        var config = new ServerConfig { CullOrphanedEntities = false, Spawning = PermissionLevel.Operator };
        using var world = new World(config);
        var flooder = world.Join(76561198000000009, "Flooder");
        flooder.FinishLoading();

        flooder.SendMany(Enumerable.Range(0, 3400).Select(i => FusionProtocol.BuildSpawnRequest(
            flooder.SmallId, "SLZ.BONELAB.Content.Avatar.FordBW", new Vec3(0, 0, 0), (uint)i)));

        Assert.DoesNotContain(flooder, world.Players);
    }

    [Fact]
    public void A_flooder_kicked_mid_sync_is_dropped_before_the_round_ends()
    {
        var config = new ServerConfig { CullOrphanedEntities = false, Spawning = PermissionLevel.Operator };
        using var world = new World(config);
        var flooder = world.Join(76561198000000009, "Flooder");
        flooder.FinishLoading();
        var other = world.Join(76561198000000001, "Other");
        other.FinishLoading();

        // Queued directly, not through SendMany, so nothing is received yet: the kick
        // has to land inside Spawn's own Sync round, not before it starts.
        foreach (byte[] spam in Enumerable.Range(0, 3400).Select(i => FusionProtocol.BuildSpawnRequest(
            flooder.SmallId, "SLZ.BONELAB.Content.Avatar.FordBW", new Vec3(0, 0, 0), (uint)i)))
        {
            world.Transport.Deliver(flooder.Connection, spam);
        }

        // Other's spawn makes the flooder raise a data request in round 0, so Sync's
        // Server.Receive there drains the queue and kicks the flooder mid-loop.
        world.Spawn(other, 300, "Pack.Spawnable.Crate", 1, 2, 3);

        Assert.DoesNotContain(flooder, world.Players);
    }

    [Fact]
    public void A_second_join_past_max_players_is_refused_as_server_full()
    {
        var config = new ServerConfig { CullOrphanedEntities = false, MaxPlayers = 1 };
        using var world = new World(config);
        world.Join(76561198000000001, "First");

        var ex = Assert.Throws<InvalidOperationException>(() => world.Join(76561198000000002, "Second"));
        Assert.Contains("Second", ex.Message);

        var closed = Assert.Single(world.Transport.Closed);
        Assert.Equal("Server full", closed.Reason);
    }

    [Fact]
    public void Grabbing_someone_elses_crate_asks_for_ownership_and_the_server_agrees()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);

        kanza.Grab(300);

        Assert.Equal(kanza.SmallId, world.Server.Entities.Get(300)!.OwnerSmallId);
    }

    [Fact]
    public void Grabbing_a_vehicle_locked_in_this_players_view_asks_for_no_ownership()
    {
        const ushort car = 400;
        using var world = new World();
        var driver = world.Join(76561198000000001, "s1mple");
        var bystander = world.Join(76561198000000002, "FOLZY");
        driver.FinishLoading();
        bystander.FinishLoading();
        bystander.View.DriverLockedVehicles.Add(car);

        world.Spawn(driver, car, "BaBaCorp.AssortedAutomobiles.Spawnable.SendalSopperSedan", 0, 0, 0);

        // Fed to the bystander's own view only, never sent to the server: the server
        // never seats the driver, so a request that did go out would be granted.
        bystander.View.Receive(FusionProtocol.BuildSeat(driver.SmallId, car, 0, true));

        bystander.Grab(car);

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(car)!.OwnerSmallId);
    }

    [Fact]
    public void AgreeOnOwner_of_an_id_no_one_has_spawned_is_false()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        Assert.False(world.AgreeOnOwner(999));
    }

    [Fact]
    public void A_late_joiner_learns_who_owns_a_prop()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Server.Entities.Register(300, "Pack.Spawnable.Crate", joel.SmallId, 1, 2, 3);

        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        Assert.Equal(joel.SmallId, kanza.View.Entities[300].Owner);
    }

    [Fact]
    public void Moving_the_clock_runs_the_resends_after_loading()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Server.Entities.Register(301, "Pack.Spawnable.Gun", joel.SmallId, 0, 0, 0);
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, 301, 0));

        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        int ModuleMessages() => world.Transport.SentTo(kanza.Connection).Count(m => m.Message[0] == ModuleProtocol.TagModule);
        int before = ModuleMessages();

        world.Advance(TimeSpan.FromSeconds(7));

        Assert.True(ModuleMessages() > before, "no holster was sent again after loading");
    }

    [Fact]
    public void A_player_sees_its_own_holster_like_everyone_else()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();
        world.Server.Entities.Register(301, "Pack.Spawnable.Gun", joel.SmallId, 0, 0, 0);

        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, 301, 0));

        Assert.Equal((ushort)301, joel.View.Slots[(joel.SmallId, 0)]);
        Assert.Equal((ushort)301, kanza.View.Slots[(joel.SmallId, 0)]);
        Assert.True(world.SlotsAgree(), "holster slots differ between players");
    }

    [Fact]
    public void A_player_who_leaves_is_gone_from_the_server()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");

        world.Leave(joel, "Closing Connection");

        Assert.Null(world.Server.Players.GetByPlatformId(76561198000000001));
    }

    [Fact]
    public void A_prop_spawned_while_players_are_here_is_seen_by_them_all()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(kanza, 300, "Pack.Spawnable.Crate", 1, 2, 3);

        Assert.Equal(kanza.SmallId, joel.View.Entities[300].Owner);
        Assert.Equal(kanza.SmallId, kanza.View.Entities[300].Owner);
    }

    [Fact]
    public void Building_someone_elses_prop_asks_them_for_its_state()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);

        Assert.Contains(world.Transport.SentTo(joel.Connection), sent =>
            Envelope.Read(sent.Message) is { } envelope
            && envelope.Tag == FusionProtocol.TagEntityDataRequest
            && envelope.Sender == kanza.SmallId);

        Assert.DoesNotContain(world.Transport.SentTo(kanza.Connection), sent =>
            Envelope.Read(sent.Message) is { } envelope
            && envelope.Tag == FusionProtocol.TagEntityDataRequest
            && envelope.Sender == joel.SmallId);
    }
}
