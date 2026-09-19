using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

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
}
