using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// The client rules a view applies, each from the LabFusion 1.14.1 decompile:
/// SpawnResponseMessage, DespawnResponseMessage, EntityOwnershipResponseMessage,
/// EntityPoseUpdateMessage, EntityCullStatusMessage, PlayerRepSeatMessage, AtvExtender
/// and the InventorySlot messages.
/// </summary>
public class ClientViewTests
{
    private static ClientView Loaded(byte smallId = 5)
    {
        var view = new ClientView(smallId);
        view.MarkLoaded();
        return view;
    }

    private static byte[] Spawn(ushort entity, byte owner)
        => FusionProtocol.BuildSpawnResponse(owner, owner, entity, "Pack.Spawnable.Crate",
            new Vec3(0, 0, 0), Array.Empty<byte>(), uint.MaxValue, spawnEffect: false, source: 0);

    [Fact]
    public void A_spawn_creates_the_entity_with_its_owner()
    {
        var view = Loaded();
        view.Receive(Spawn(300, 1));

        Assert.Equal((byte)1, view.Entities[300].Owner);
    }

    [Fact]
    public void A_spawn_while_loading_lands_once_loaded()
    {
        var view = new ClientView(5);
        view.Receive(Spawn(300, 1));

        Assert.Empty(view.Entities);

        view.MarkLoaded();

        Assert.True(view.Entities.ContainsKey(300));
    }

    [Fact]
    public void An_ownership_response_changes_the_owner()
    {
        var view = Loaded();
        view.Receive(Spawn(300, 1));
        view.Receive(FusionProtocol.BuildOwnershipResponse(2, 300));

        Assert.Equal((byte)2, view.Entities[300].Owner);
    }

    [Fact]
    public void A_vehicle_locked_to_its_driver_ignores_ownership_responses()
    {
        var view = Loaded();
        view.DriverLockedVehicles.Add(400);
        view.Receive(Spawn(400, 1));
        view.Receive(FusionProtocol.BuildSeat(rider: 2, seatId: 400, index: 0, ingress: true));
        view.Receive(FusionProtocol.BuildOwnershipResponse(3, 400));

        Assert.Equal((byte)2, view.Entities[400].Owner);
    }

    [Fact]
    public void A_pose_counts_only_from_the_owner()
    {
        var view = Loaded();
        view.Receive(Spawn(300, 1));

        view.Receive(FusionProtocol.BuildEntityPoseUpdate(2, 300, new Vec3(1, 1, 1), default, default, default));

        Assert.Equal(1, view.PosesRejected);
    }

    [Fact]
    public void A_pose_while_loading_is_thrown_away()
    {
        var view = new ClientView(5);
        view.Receive(FusionProtocol.BuildEntityPoseUpdate(1, 300, new Vec3(1, 1, 1), default, default, default));

        Assert.Equal(1, view.DroppedWhileLoading);
    }

    [Fact]
    public void A_seat_puts_the_rider_in_it_and_getting_out_empties_it()
    {
        var view = Loaded();
        view.Receive(Spawn(400, 1));
        view.Receive(FusionProtocol.BuildSeat(2, 400, 1, true));

        Assert.Equal(((ushort)400, (byte)1), view.Seats[2]);

        view.Receive(FusionProtocol.BuildSeat(2, 400, 1, false));

        Assert.False(view.Seats.ContainsKey(2));
    }

    [Fact]
    public void A_slot_insert_holsters_the_item_and_a_drop_takes_it_out()
    {
        var view = Loaded();
        view.Receive(ClientMessages.SlotInsert(3, slot: 3, weapon: 500, index: 0));

        Assert.Equal((ushort)500, view.Slots[((ushort)3, (byte)0)]);

        view.Receive(ClientMessages.SlotDrop(3, slot: 3, grabber: 3, index: 0, hand: 2));

        Assert.False(view.Slots.ContainsKey(((ushort)3, (byte)0)));
    }

    [Fact]
    public void An_item_is_in_one_slot_at_a_time()
    {
        var view = Loaded();
        view.Receive(ClientMessages.SlotInsert(3, slot: 3, weapon: 500, index: 0));
        view.Receive(ClientMessages.SlotInsert(4, slot: 4, weapon: 500, index: 0));

        Assert.False(view.Slots.ContainsKey(((ushort)3, (byte)0)));
        Assert.Equal((ushort)500, view.Slots[((ushort)4, (byte)0)]);
    }

    [Fact]
    public void A_despawn_removes_the_entity()
    {
        var view = Loaded();
        view.Receive(Spawn(300, 1));
        view.Receive(ServerProtocol.WriteDespawnResponse(2, 300, false));

        Assert.False(view.Entities.ContainsKey(300));
    }

    [Fact]
    public void A_cull_status_counts_only_from_the_owner()
    {
        var view = Loaded();
        view.Receive(Spawn(302, 1));

        view.Receive(ClientMessages.CullStatus(2, 302, true));
        Assert.False(view.Entities[302].CulledForOwner);

        view.Receive(ClientMessages.CullStatus(1, 302, true));
        Assert.True(view.Entities[302].CulledForOwner);
    }

    [Fact]
    public void Building_a_prop_owned_by_someone_else_asks_them_for_its_state()
    {
        var view = Loaded();
        view.Receive(Spawn(300, 1));

        Assert.Equal(new[] { ((ushort)300, (byte)1) }, view.TakeDataRequests());
    }

    [Fact]
    public void Building_a_prop_the_player_owns_asks_nobody()
    {
        var view = Loaded(smallId: 1);
        view.Receive(Spawn(300, 1));

        Assert.Empty(view.TakeDataRequests());
    }

    [Fact]
    public void A_prop_built_from_a_held_spawn_asks_for_its_state_only_once_loaded()
    {
        var view = new ClientView(5);
        view.Receive(Spawn(300, 1));

        Assert.Empty(view.TakeDataRequests());

        view.MarkLoaded();

        Assert.Equal(new[] { ((ushort)300, (byte)1) }, view.TakeDataRequests());
    }

    [Fact]
    public void Taking_data_requests_empties_the_list()
    {
        var view = Loaded();
        view.Receive(Spawn(300, 1));
        view.TakeDataRequests();

        Assert.Empty(view.TakeDataRequests());
    }
}
