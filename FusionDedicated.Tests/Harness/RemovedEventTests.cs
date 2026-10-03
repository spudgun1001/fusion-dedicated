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
        Assert.Equal(JoelId, gone.OwnerPlatformId);
    }

    [Fact]
    public void A_refusing_plugin_does_not_hide_news_from_the_next()
    {
        using var world = new World();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        var heard = new List<string>();
        events.Joined.Subscribe("grump", _ => PluginVerdict.Refuse("no"));
        events.Removed.Subscribe("grump", _ => PluginVerdict.Refuse("no"));
        events.Left.Subscribe("grump", _ => PluginVerdict.Refuse("no"));
        events.Joined.Subscribe("watcher", _ => { heard.Add("joined"); return PluginVerdict.Allow; });
        events.Removed.Subscribe("watcher", _ => { heard.Add("removed"); return PluginVerdict.Allow; });
        events.Left.Subscribe("watcher", _ => { heard.Add("left"); return PluginVerdict.Allow; });
        world.Server.Plugins = events;

        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 400, "Pack.Spawnable.Crate", 0, 0, 0);
        world.Server.RemoveEntity(400);
        world.Leave(joel, "left");

        Assert.Equal(new[] { "joined", "removed", "left" }, heard);
    }
}
