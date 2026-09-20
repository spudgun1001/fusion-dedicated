using System.Text.Json;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Player 0's connection response, read back with Fusion's own reader in the order
/// ConnectionResponseData and PlayerID write it.
/// </summary>
public class ServerPlayerWireTests
{
    private const ulong ServerSteamId = 90071992547409920;

    [Fact]
    public void Fusions_reader_gets_back_every_field_of_player_0()
    {
        byte[] message = ServerPlayer.ConnectionResponse(ServerSteamId, "Southside RP");
        var reader = new OracleReader(message);

        Assert.Equal(FusionProtocol.TagConnectionResponse, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte()); // RelayType.None, so no sender follows
        reader.ReadByte();                  // channel
        Assert.Equal(message.Length - reader.Position - sizeof(int), reader.ReadInt32());

        Assert.Equal(ServerSteamId, reader.ReadUInt64());
        Assert.Equal(0, reader.ReadByte());

        int count = reader.ReadInt32();
        var metadata = new Dictionary<string, string?>();

        for (var i = 0; i < count; i++)
        {
            metadata[reader.ReadString()!] = reader.ReadString();
        }

        Assert.Equal("Southside RP", metadata["Username"]);
        Assert.Equal("", metadata["Nickname"]);
        Assert.Equal("true", metadata["Loading"]);
        Assert.Equal("", metadata["LevelBarcode"]);
        Assert.Equal("OWNER", metadata["PermissionLevel"]);

        Assert.Equal(0, reader.ReadInt32()); // no cosmetics
        Assert.Equal("c3534c5a-94b2-40a4-912a-24a8506f6c79", reader.ReadString());

        for (var i = 0; i < FusionProtocol.AvatarStatFloatCount; i++)
        {
            Assert.Equal(1f, reader.ReadSingle());
        }

        Assert.False(reader.ReadBoolean());
        Assert.Equal(message.Length, reader.Position);
    }

    /// <summary>
    /// Fusion reads Loading with System.Text.Json, which only takes lowercase JSON
    /// booleans. A capitalised value throws and kills the client's rig coroutine.
    /// </summary>
    [Fact]
    public void Fusion_can_json_parse_player_0s_loading_value()
    {
        string loading = MetadataOf(ServerPlayer.ConnectionResponse(ServerSteamId, "Southside RP"))["Loading"]!;

        Assert.True(JsonSerializer.Deserialize<bool>(loading));
    }

    private static Dictionary<string, string?> MetadataOf(byte[] message)
    {
        var reader = new OracleReader(message);

        reader.ReadByte();   // tag
        reader.ReadByte();   // route
        reader.ReadByte();   // channel
        reader.ReadInt32();  // length
        reader.ReadUInt64(); // platform id
        reader.ReadByte();   // small id

        int count = reader.ReadInt32();
        var metadata = new Dictionary<string, string?>();

        for (var i = 0; i < count; i++)
        {
            metadata[reader.ReadString()!] = reader.ReadString();
        }

        return metadata;
    }
}
