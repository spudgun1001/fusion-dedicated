namespace FusionDedicated.Tests.Harness;

public class PluginSpawnForTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    [Fact]
    public void A_crate_spawned_for_a_player_is_theirs_on_every_client()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Crate", 1f, 2f, 3f, Array.Empty<byte>(), KanzaId);
        world.Sync();

        Assert.NotEqual((ushort)0, id);
        Assert.Equal((byte?)kanza.SmallId, world.Server.Entities.Get(id)!.OwnerSmallId);
        Assert.Equal((byte?)kanza.SmallId, joel.View.Entities[id].Owner);
        Assert.Equal((byte?)kanza.SmallId, kanza.View.Entities[id].Owner);
    }

    [Fact]
    public void Nothing_is_spawned_for_a_player_who_is_not_here()
    {
        using var world = new World();
        world.Join(JoelId, "Joel").FinishLoading();

        Assert.Equal((ushort)0,
            world.Server.SpawnForPlayer("Pack.Spawnable.Crate", 0f, 0f, 0f, Array.Empty<byte>(), 76561198000000099));
    }

    [Fact]
    public void Nothing_is_spawned_without_a_barcode()
    {
        using var world = new World();
        world.Join(JoelId, "Joel").FinishLoading();

        Assert.Equal((ushort)0, world.Server.SpawnForPlayer(" ", 0f, 0f, 0f, Array.Empty<byte>(), JoelId));
    }
}
