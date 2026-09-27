using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// ServerSettings carries the whole player list and went to everybody every tick. Fusion's own host
/// sends it only on a join or a change, so each player is sent it only when their copy is out of date.
/// </summary>
public class ServerSettingsRepeatTests
{
    private static int Settings(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection).Count(s => s.Message[0] == ServerProtocol.TagServerSettings);

    [Fact]
    public void A_tick_with_nothing_changed_sends_no_settings()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var joel = world.Join(76561198000000001, "Joel");
        int before = Settings(world, joel);

        world.Tick();
        world.Tick();

        Assert.Equal(before, Settings(world, joel));
    }

    [Fact]
    public void A_joiner_is_sent_the_settings_once()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var joel = world.Join(76561198000000001, "Joel");

        Assert.Equal(1, Settings(world, joel));
    }

    [Fact]
    public void Somebody_joining_sends_everyone_else_the_new_player_list()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var joel = world.Join(76561198000000001, "Joel");
        int before = Settings(world, joel);

        world.Join(76561198000000002, "Kanza");

        Assert.Equal(before + 1, Settings(world, joel));
    }

    [Fact]
    public void A_changed_setting_reaches_everyone_once()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var joel = world.Join(76561198000000001, "Joel");
        int before = Settings(world, joel);

        world.Server.Config.Mortality = !world.Server.Config.Mortality;
        world.Server.PushSettings();
        world.Tick();

        Assert.Equal(before + 1, Settings(world, joel));
    }

    [Fact]
    public void A_refused_settings_send_is_tried_again_on_the_next_tick()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, SendRetryQueue = 0 });
        var joel = world.Join(76561198000000001, "Joel");
        int before = Settings(world, joel);

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.Config.Mortality = !world.Server.Config.Mortality;
        world.Server.PushSettings();
        world.Transport.FailSendsWith = null;

        world.Tick();

        Assert.Equal(before + 1, Settings(world, joel));
    }
}
