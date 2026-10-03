using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Plugins hear every prop that leaves, with its barcode, its owner and why it went.</summary>
public class RemovedEventTests
{
    private const ulong JoelId = 76561198000000001, KanzaId = 76561198000000002;

    private static List<RemovedEvent> Listen(World world)
    {
        var heard = new List<RemovedEvent>();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Removed.Subscribe("watcher", e => { heard.Add(e); return PluginVerdict.Allow; });
        world.Server.Plugins = events;
        return heard;
    }

    [Fact]
    public void A_plugin_hears_a_despawn_with_the_barcode_and_owner()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 400, "Pack.Spawnable.Crate", 0, 0, 0);
        var heard = Listen(world);

        world.Server.RemoveEntity(400);

        Assert.Equal(new RemovedEvent(400, "Pack.Spawnable.Crate", JoelId, RemovalReason.Despawned), Assert.Single(heard));
    }

    [Fact]
    public void A_leavers_holstered_gun_is_reported_as_left()
    {
        const ushort Holstered = 503;
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, OwnershipRequestsPerSecond = 0 });
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();
        world.Spawn(joel, Holstered, "Pack.Spawnable.Pistol", 0, 0, 0);
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, Holstered, 1));
        var heard = Listen(world);

        world.Leave(joel, "left");

        var gone = Assert.Single(heard, e => e.EntityId == Holstered);
        Assert.Equal(RemovalReason.Left, gone.Reason);
        Assert.Equal("Pack.Spawnable.Pistol", gone.Barcode);
    }
}
