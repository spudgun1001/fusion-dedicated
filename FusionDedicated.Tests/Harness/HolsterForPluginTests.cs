namespace FusionDedicated.Tests.Harness;

public class HolsterForPluginTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    [Fact]
    public void A_plugin_holsters_an_item_into_the_players_own_slot_for_everyone()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);
        world.Sync();

        Assert.True(world.Server.HolsterForPlugin(id, KanzaId, 3));
        world.Sync();

        Assert.Equal(id, kanza.View.Slots[((ushort)kanza.SmallId, (byte)3)]);
        Assert.Equal(id, joel.View.Slots[((ushort)kanza.SmallId, (byte)3)]);
    }

    [Fact]
    public void A_player_joining_later_sees_the_plugin_holstered_item()
    {
        using var world = new World();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);
        world.Sync();
        world.Server.HolsterForPlugin(id, KanzaId, 1);

        var late = world.Join(JoelId, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(id, late.View.Slots[((ushort)kanza.SmallId, (byte)1)]);
    }

    [Fact]
    public void Holstering_for_a_missing_player_or_entity_is_refused()
    {
        using var world = new World();
        world.Join(KanzaId, "Kanza").FinishLoading();

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);

        Assert.False(world.Server.HolsterForPlugin(id, 76561198000000099, 1));
        Assert.False(world.Server.HolsterForPlugin(9999, KanzaId, 1));
    }
}
