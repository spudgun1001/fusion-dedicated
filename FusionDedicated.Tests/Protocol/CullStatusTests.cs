using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Telling somebody who has just joined that a prop's owner is not simulating it.
///
/// A prop the owner has culled is frozen: nothing moves it, so it hangs wherever
/// it was. A client standing next to it takes it over and it falls, but only if
/// it has been told the owner is not simulating it. That is announced once, when
/// it happens, so anybody who joins afterwards never hears it and the prop hangs
/// in the air for the rest of the session.
/// </summary>
public class CullStatusTests
{
    /// <summary>An EntityCullStatus as a client sends it: ToOtherClients.</summary>
    private static byte[] FromClient(byte sender, ushort entityId, bool culled)
    {
        var payload = new FusionNetWriter(8);
        payload.WriteUInt16(entityId);
        payload.Write(culled);

        var message = new FusionNetWriter(32);
        message.Write(FusionProtocol.TagEntityCullStatus);
        message.Write((byte)3);
        message.Write((byte)0);
        message.WriteNullable(sender);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    [Fact]
    public void A_clients_cull_status_is_read()
    {
        var read = FusionProtocol.TryReadCullStatus(FromClient(3, 300, true));

        Assert.NotNull(read);
        Assert.Equal((ushort)300, read!.Value.EntityId);
        Assert.True(read.Value.Culled);
    }

    [Fact]
    public void Resuming_is_read_too()
    {
        var read = FusionProtocol.TryReadCullStatus(FromClient(3, 300, false));

        Assert.False(read!.Value.Culled);
    }

    [Fact]
    public void What_the_server_writes_it_reads_back()
    {
        var read = FusionProtocol.TryReadCullStatus(
            FusionProtocol.BuildCullStatus(owner: 3, target: 7, entityId: 300, culled: true));

        Assert.Equal((ushort)300, read!.Value.EntityId);
        Assert.True(read.Value.Culled);
    }

    [Fact]
    public void It_is_stamped_as_coming_from_the_owner_and_aimed_at_one_player()
    {
        // A client only believes a cull status from the entity's current owner,
        // so one sent as coming from the server is thrown away without a word.
        var message = FusionProtocol.BuildCullStatus(owner: 3, target: 7, entityId: 300, culled: true);

        var reader = new OracleReader(message);

        Assert.Equal(FusionProtocol.TagEntityCullStatus, reader.ReadByte());
        Assert.Equal(4, reader.ReadByte());              // ToTarget
        Assert.Equal(0, reader.ReadByte());              // reliable
        Assert.Equal((byte)7, reader.ReadNullableByte()); // the newcomer
        Assert.Equal((byte)3, reader.ReadNullableByte()); // the owner
        Assert.Equal(3, reader.ReadInt32());             // entity and flag
    }

    [Fact]
    public void The_registry_remembers_it_per_entity()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Gun", 3, 0, 0, 0);
        registry.Register(301, "Pack.Spawnable.Gun", 3, 0, 0, 0);

        registry.SetCulledForOwner(300, true);

        Assert.True(registry.Get(300)!.CulledForOwner);
        Assert.False(registry.Get(301)!.CulledForOwner);
    }

    [Fact]
    public void An_owner_that_starts_simulating_again_clears_it()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Gun", 3, 0, 0, 0);

        registry.SetCulledForOwner(300, true);
        registry.SetCulledForOwner(300, false);

        Assert.False(registry.Get(300)!.CulledForOwner);
    }

    [Fact]
    public void Nothing_happens_for_an_entity_that_is_not_here()
    {
        var registry = new EntityRegistry();

        registry.SetCulledForOwner(999, true);

        Assert.Null(registry.Get(999));
    }

    [Fact]
    public void A_truncated_message_is_refused_rather_than_guessed_at()
        => Assert.Null(FusionProtocol.TryReadCullStatus(new byte[] { 80, 3, 0 }));
}
