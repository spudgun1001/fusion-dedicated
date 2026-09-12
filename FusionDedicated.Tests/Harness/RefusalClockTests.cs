using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Refusal windows open and close on the server clock, so a test clock behind wall time still gets the summary line.</summary>
public class RefusalClockTests
{
    [Fact]
    public void Held_back_refusals_are_summed_up_once_the_window_closes()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, Spawning = PermissionLevel.Operator });
        var freeman = world.Join(76561198000000009, "Mr.freeman");
        freeman.FinishLoading();

        freeman.SendMany(Enumerable.Range(0, 3).Select(i => FusionProtocol.BuildSpawnRequest(
            freeman.SmallId, "SLZ.BONELAB.Content.Avatar.FordBW", new Vec3(0, 0, 0), (uint)i)));

        world.Advance(TimeSpan.FromSeconds(6));
        world.Tick();

        Assert.Contains(world.Server.RecentLog(2000), e => e.Message.Contains("held back from the log"));
    }
}
