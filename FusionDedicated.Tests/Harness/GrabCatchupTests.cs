using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Fusion asks the holder for a grab a joiner missed, and that answer does not always
/// arrive, so the item hangs in the air and follows nobody.
/// </summary>
public class GrabCatchupTests
{
    private const ushort Gun = 600;
    private const ushort Pistol = 601;
    private const string Barcode = "spudgun1001.Guns.Spawnable.Eder22";

    private const byte Right = (byte)FusionProtocol.Handedness.RIGHT;
    private const byte Left = (byte)FusionProtocol.Handedness.LEFT;

    private static int GrabsTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection).Count(s => s.Message[0] == FusionProtocol.TagPlayerRepGrab);

    /// <returns>Where a tag first reached a player, or -1.</returns>
    private static int FirstTo(World world, FakePlayer player, byte tag)
        => world.Transport.SentTo(player.Connection).ToList().FindIndex(s => s.Message[0] == tag);

    private static FakePlayer JoinLate(World world)
    {
        var late = world.Join(76561198000000009, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        return late;
    }


    /// <returns>The last grab a player was sent, or null.</returns>
    private static byte[]? LastGrabTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection)
            .LastOrDefault(s => s.Message[0] == FusionProtocol.TagPlayerRepGrab).Message;

    /// <summary>A holder with a gun in hand, paced slowly enough that a joiner's replay waits in the queue.</summary>
    private static (World World, FakePlayer Holder, FakePlayer Late) QueuedReplay()
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = false, CatchupMessagesPerSecond = 1 });
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        world.Spawn(holder, Gun, Barcode, 0, 0, 0);
        holder.Grab(Gun);

        var late = world.Join(76561198000000009, "Late");
        late.FinishLoading();

        Assert.Equal(0, GrabsTo(world, late));

        return (world, holder, late);
    }

    [Fact]
    public void A_joiner_is_told_the_gun_is_in_the_hand_that_holds_it()
    {
        using var world = new World();
        var bystander = world.Join(76561198000000001, "Bystander");
        bystander.FinishLoading();
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        world.Spawn(holder, Gun, Barcode, 0, 0, 0);
        holder.Grab(Gun);

        int bystanderSaw = GrabsTo(world, bystander);
        var late = JoinLate(world);

        Assert.Equal(Gun, late.View.Held[(holder.SmallId, Right)]);
        Assert.Equal(0, late.View.GrabsForUnknownEntities);
        Assert.Equal(bystanderSaw, GrabsTo(world, bystander));
        Assert.True(FirstTo(world, late, FusionProtocol.TagPlayerRepGrab)
                    > FirstTo(world, late, FusionProtocol.TagSpawnResponse));
    }

    [Fact]
    public void A_gun_in_each_hand_reaches_a_joiner_as_two_grabs()
    {
        using var world = new World();
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        world.Spawn(holder, Gun, Barcode, 0, 0, 0);
        world.Spawn(holder, Pistol, Barcode, 1, 0, 0);
        holder.Grab(Gun);
        holder.Grab(Pistol, FusionProtocol.Handedness.LEFT);

        var late = JoinLate(world);

        Assert.Equal(Gun, late.View.Held[(holder.SmallId, Right)]);
        Assert.Equal(Pistol, late.View.Held[(holder.SmallId, Left)]);
    }

    [Fact]
    public void Both_players_holding_one_crate_reach_a_joiner()
    {
        using var world = new World();
        var first = world.Join(76561198000000001, "First");
        first.FinishLoading();
        var second = world.Join(76561198000000002, "Second");
        second.FinishLoading();

        world.Spawn(first, Gun, Barcode, 0, 0, 0);
        first.Grab(Gun);
        second.Grab(Gun, FusionProtocol.Handedness.LEFT);

        var late = JoinLate(world);

        Assert.Equal(Gun, late.View.Held[(first.SmallId, Right)]);
        Assert.Equal(Gun, late.View.Held[(second.SmallId, Left)]);
    }

    [Fact]
    public void A_gun_let_go_of_before_the_joiner_arrives_is_not_replayed()
    {
        using var world = new World();
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        world.Spawn(holder, Gun, Barcode, 0, 0, 0);
        holder.Grab(Gun);
        holder.Send(FusionProtocol.BuildRelease(holder.SmallId, FusionProtocol.Handedness.RIGHT));

        var late = JoinLate(world);

        Assert.Empty(late.View.Held);
        Assert.Equal(0, GrabsTo(world, late));
    }

    [Fact]
    public void Asking_about_a_holders_rig_replays_both_their_hands()
    {
        using var world = new World();
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        world.Spawn(holder, Gun, Barcode, 0, 0, 0);
        world.Spawn(holder, Pistol, Barcode, 1, 0, 0);
        holder.Grab(Gun);
        holder.Grab(Pistol, FusionProtocol.Handedness.LEFT);

        var late = JoinLate(world);
        int answered = GrabsTo(world, late);

        // What a real client sends once it has built somebody's rig, which is the
        // first moment it can attach a grab to them.
        late.Send(FusionProtocol.BuildEntityDataRequest(late.SmallId, holder.SmallId, holder.SmallId));
        world.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(answered + 2, GrabsTo(world, late));
    }

    [Fact]
    public void A_rig_request_naming_the_asker_replays_nothing()
    {
        using var world = new World();
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        world.Spawn(holder, Gun, Barcode, 0, 0, 0);
        holder.Grab(Gun);

        int seen = GrabsTo(world, holder);
        holder.Send(FusionProtocol.BuildEntityDataRequest(holder.SmallId, holder.SmallId, holder.SmallId));
        world.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(seen, GrabsTo(world, holder));
    }

    [Fact]
    public void The_joiner_gets_the_bytes_the_holder_sent()
    {
        using var world = new World();
        var bystander = world.Join(76561198000000001, "Bystander");
        bystander.FinishLoading();
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        world.Spawn(holder, Gun, Barcode, 0, 0, 0);

        // A grip well away from the defaults, so a rebuilt message would differ here.
        holder.Send(FusionProtocol.BuildGrab(holder.SmallId, FusionProtocol.Handedness.RIGHT, 5, Gun,
            new Vec3(0.25f, -1.5f, 2f), new Quat(0.1f, 0.2f, 0.3f, 0.927f)));

        var late = JoinLate(world);

        Assert.Equal(LastGrabTo(world, bystander), LastGrabTo(world, late));
    }

    [Fact]
    public void A_regrab_before_the_replay_is_sent_goes_out_at_the_new_grip()
    {
        var (world, holder, late) = QueuedReplay();
        using var disposing = world;

        byte[] regrab = FusionProtocol.BuildGrab(holder.SmallId, FusionProtocol.Handedness.RIGHT, 5, Gun,
            new Vec3(0.25f, -1.5f, 2f), new Quat(0.1f, 0.2f, 0.3f, 0.927f));

        holder.Send(regrab);
        world.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(regrab, LastGrabTo(world, late));
    }

    [Fact]
    public void A_gun_despawned_before_the_replay_is_sent_is_not_replayed()
    {
        var (world, holder, late) = QueuedReplay();
        using var disposing = world;

        holder.Send(ClientMessages.Despawn(holder.SmallId, Gun));
        world.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, GrabsTo(world, late));
    }

    [Fact]
    public void A_gun_let_go_of_before_the_replay_is_sent_is_not_replayed()
    {
        var (world, holder, late) = QueuedReplay();
        using var disposing = world;

        holder.Send(FusionProtocol.BuildRelease(holder.SmallId, FusionProtocol.Handedness.RIGHT));
        world.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, GrabsTo(world, late));
    }

    [Fact]
    public void A_holder_who_leaves_before_the_replay_is_sent_is_not_replayed()
    {
        var (world, holder, late) = QueuedReplay();
        using var disposing = world;

        world.Leave(holder, "quit");
        world.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, GrabsTo(world, late));
    }

    [Fact]
    public void A_grab_of_an_id_the_server_never_registered_is_not_recorded()
    {
        using var world = new World();
        var holder = world.Join(76561198000000002, "Holder");
        holder.FinishLoading();

        holder.Send(FusionProtocol.BuildGrab(holder.SmallId, FusionProtocol.Handedness.RIGHT, 0, 999));

        Assert.Empty(world.Server.HoldersOf(999));
    }
}
