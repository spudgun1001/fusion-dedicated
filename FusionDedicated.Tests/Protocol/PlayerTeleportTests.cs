using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Teleporting never worked. The client addresses the command to the server, and
/// the server answered by handing it to Relay, which drops anything addressed to
/// itself, so nothing was ever sent. A real host reads the rig position and sends
/// a PlayerRepTeleport carrying it, which is what this builds.
/// </summary>
public class PlayerTeleportTests
{
    private static (byte Tag, byte RelayType, byte? Sender, Vec3 Position) Parse(byte[] message)
    {
        var reader = new FusionNetReader(message);

        byte tag = reader.ReadByte();
        byte relayType = reader.ReadByte();
        reader.ReadByte();                      // channel
        byte? sender = reader.ReadNullableByte();
        reader.ReadInt32();                     // payload length

        return (tag, relayType, sender,
            new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
    }

    [Fact]
    public void The_message_is_a_player_rep_teleport()
    {
        Assert.Equal(GateProtocol.TagPlayerRepTeleport,
            Parse(ServerProtocol.WritePlayerTeleport(3, new Vec3(1, 2, 3))).Tag);
    }

    [Fact]
    public void It_carries_the_position_it_was_given()
    {
        var parsed = Parse(ServerProtocol.WritePlayerTeleport(3, new Vec3(1.5f, -2.5f, 3.5f)));

        Assert.Equal(new Vec3(1.5f, -2.5f, 3.5f), parsed.Position);
    }

    [Fact]
    public void It_is_stamped_with_a_real_player_so_the_client_can_resolve_it()
    {
        // Clients ignore a message from a sender they cannot resolve, which is the
        // same trap the despawn broadcast had to work around.
        Assert.Equal((byte)3, Parse(ServerProtocol.WritePlayerTeleport(3, Vec3.Zero)).Sender);
    }

    [Fact]
    public void It_is_addressed_to_clients_rather_than_back_to_the_server()
    {
        // Relay throws away anything routed to the server, which is exactly how
        // teleporting came to do nothing at all.
        Assert.Equal(2, Parse(ServerProtocol.WritePlayerTeleport(3, Vec3.Zero)).RelayType);
    }

    [Fact]
    public void Teleporting_to_them_moves_the_asker_to_the_other_player()
    {
        var plan = TeleportPlanner.For(
            ServerProtocol.PermissionCommand.TeleportToThem,
            asker: 1, askerAt: new Vec3(10, 0, 10),
            other: 2, otherAt: new Vec3(20, 0, 20));

        Assert.NotNull(plan);
        Assert.Equal((byte)1, plan!.Value.MoveSmallId);
        Assert.Equal(new Vec3(20, 0, 20), plan.Value.To);
    }

    [Fact]
    public void Teleporting_to_me_moves_the_other_player_to_the_asker()
    {
        var plan = TeleportPlanner.For(
            ServerProtocol.PermissionCommand.TeleportToMe,
            asker: 1, askerAt: new Vec3(10, 0, 10),
            other: 2, otherAt: new Vec3(20, 0, 20));

        Assert.NotNull(plan);
        Assert.Equal((byte)2, plan!.Value.MoveSmallId);
        Assert.Equal(new Vec3(10, 0, 10), plan.Value.To);
    }

    [Fact]
    public void Any_other_command_is_not_a_teleport()
    {
        Assert.Null(TeleportPlanner.For(
            ServerProtocol.PermissionCommand.Kick,
            asker: 1, askerAt: Vec3.Zero, other: 2, otherAt: Vec3.Zero));
    }
}
