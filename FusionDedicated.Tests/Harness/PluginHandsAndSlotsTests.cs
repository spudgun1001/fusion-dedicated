using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

public class PluginHandsAndSlotsTests
{
    private const ulong KanzaId = 76561198000000002;

    [Fact]
    public void What_a_player_holds_and_holsters_is_listed_for_plugins()
    {
        using var world = new World();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        world.Spawn(kanza, 400, "Pack.Spawnable.Pistol", 0, 1, 0);
        world.Spawn(kanza, 401, "Pack.Spawnable.Knife", 0, 1, 0);

        kanza.Send(FusionProtocol.BuildGrab(kanza.SmallId, FusionProtocol.Handedness.RIGHT, 0, 400));
        kanza.Send(ClientMessages.SlotInsert(kanza.SmallId, kanza.SmallId, 401, 2));
        world.Sync();

        Assert.Equal(new ushort[] { 400 }, world.Server.HeldBy(KanzaId));
        Assert.Equal(new[] { new PluginSlot(2, 401) }, world.Server.HolsteredBy(KanzaId));
    }

    [Fact]
    public void Nobody_here_holds_or_holsters_anything()
    {
        using var world = new World();

        Assert.Empty(world.Server.HeldBy(KanzaId));
        Assert.Empty(world.Server.HolsteredBy(KanzaId));
    }
}
