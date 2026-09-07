using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Messages only a server may send.
///
/// Fusion refuses these on the receiving side: a handler marked ClientsOnly
/// throws when it sees one the receiver handled as the server, and the throw is
/// swallowed. So in a normal lobby a client forging one achieves nothing.
///
/// A relay forwards on the route byte alone, which handed every one of them
/// straight through. SpawnResponse alone puts any barcode in front of every
/// player without passing the blocklist, the tool gate, the rank check, the rate
/// limiter, the entity cap or any plugin.
/// </summary>
public class HostOnlyTagTests
{
    /// <summary>
    /// The tags Fusion marks ClientsOnly, plus the two only a host sends.
    /// Read out of the decompiled mod rather than guessed.
    /// </summary>
    public static readonly byte[] HostOnly =
    {
        2,    // ConnectionResponse
        3,    // Disconnect
        12,   // SceneLoad
        14,   // EntityUnqueueResponse
        16,   // EntityOwnershipResponse
        21,   // SpawnResponse
        23,   // DespawnResponse
        45,   // ServerSettings
        60,   // PlayerMetadataResponse
        69,   // PlayerRepTeleport
        201,  // DynamicsAssignment
        202,  // GamemodeMetadataSet
        203,  // GamemodeMetadataRemove
    };

    [Fact]
    public void The_list_holds_the_ones_that_matter_most()
    {
        // Named individually so a future edit that drops one is loud.
        Assert.Contains((byte)21, HostOnly);   // spawn anything, past every gate
        Assert.Contains((byte)3, HostOnly);    // remove any player
        Assert.Contains((byte)12, HostOnly);   // move the whole server
        Assert.Contains((byte)16, HostOnly);   // take an object from its holder
        Assert.Contains((byte)45, HostOnly);   // rewrite the rules on every client
    }

    [Fact]
    public void Nothing_a_client_legitimately_sends_is_on_it()
    {
        // Each of these is sent by an ordinary client in normal play. Refusing
        // one would break the game rather than protect it.
        byte[] fromClients =
        {
            1,   // ConnectionRequest
            4,   // PlayerPoseUpdate
            5,   // PlayerRepAvatar
            8,   // PlayerRepSeat
            9,   // PlayerRepGrab
            10,  // PlayerRepRelease
            13,  // EntityUnqueueRequest
            15,  // EntityOwnershipRequest
            17,  // EntityPoseUpdate
            18,  // NetworkPropCreate
            20,  // SpawnRequest
            22,  // DespawnRequest
            59,  // PlayerMetadataRequest
            64,  // PlayerRepDamage
            67,  // PlayerVoiceChat
            68,  // PermissionCommandRequest
            79,  // EntityDataRequest
            80,  // EntityCullStatus
            200, // Module
        };

        foreach (byte tag in fromClients)
        {
            Assert.DoesNotContain(tag, HostOnly);
        }
    }

    [Fact]
    public void A_forged_spawn_response_is_the_worst_of_them()
    {
        // Proving the shape of what was possible: a client could build this and
        // every player would spawn it, with the server never consulted.
        byte[] forged = FusionProtocol.BuildSpawnResponse(
            3, 3, 999, "SomePack.Spawnable.Nuke",
            new Vec3(0, 0, 0), null, 0);

        Assert.Equal(21, forged[0]);
        Assert.Contains(forged[0], HostOnly);
    }
}

/// <summary>
/// ToTargets names the players it is for. Broadcasting instead sends a message
/// meant for two people to the room.
/// </summary>
public class ToTargetsTests
{
    private static byte[] Message(params byte[] targets)
    {
        var message = new OracleWriter();
        message.Write((byte)200);   // any tag
        message.Write((byte)5);     // ToTargets
        message.Write((byte)0);     // Reliable
        message.Write(targets);     // a length prefixed block of small ids
        message.Write((byte?)1);    // sender
        message.Write(new byte[] { 9, 9 });

        return message.ToArray();
    }

    [Fact]
    public void The_named_players_are_read_back()
        => Assert.Equal(new byte[] { 3, 7 }, ServerProtocol.ReadTargets(Message(3, 7)));

    [Fact]
    public void An_empty_list_names_nobody()
        => Assert.Empty(ServerProtocol.ReadTargets(Message()));

    [Fact]
    public void Any_other_route_names_nobody()
    {
        var message = new OracleWriter();
        message.Write((byte)200);
        message.Write((byte)2);     // ToClients
        message.Write((byte)0);
        message.Write((byte?)1);
        message.Write(new byte[] { 9 });

        Assert.Empty(ServerProtocol.ReadTargets(message.ToArray()));
    }

    [Fact]
    public void Something_malformed_names_nobody_rather_than_throwing()
    {
        Assert.Empty(ServerProtocol.ReadTargets(new byte[] { 200, 5 }));
        Assert.Empty(ServerProtocol.ReadTargets(Array.Empty<byte>()));
    }
}
