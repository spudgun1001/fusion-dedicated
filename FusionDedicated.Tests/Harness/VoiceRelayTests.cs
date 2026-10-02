using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Voice was three quarters of everything sent, and every word went to every player. Fusion
/// mutes a voice past about 33 m, so a player further away than that is sent none of it.
/// </summary>
public class VoiceRelayTests
{
    private static readonly long VoiceProxyInput =
        ModuleProtocol.TagFor("LabFusion", "LabFusion.SDK.Messages.VoiceProxyInputMessage");

    private sealed record Scene(World World, FakePlayer Talker, FakePlayer Near, FakePlayer Far);

    /// <summary>A talker 200 m out, one player 10 m from them and one 100 m. An unknown position reads as the origin.</summary>
    private static Scene Build(ServerConfig? config = null, bool farHasPosition = true)
    {
        var world = new World(config ?? new ServerConfig { CullOrphanedEntities = false });
        var talker = world.Join(76561198000000001, "Dennis");
        var near = world.Join(76561198000000002, "Joel");
        var far = world.Join(76561198000000003, "Kanza");

        foreach (var player in new[] { talker, near, far })
        {
            player.FinishLoading();
        }

        StandAt(talker, new Vec3(200, 0, 0));
        StandAt(near, new Vec3(210, 0, 0));

        if (farHasPosition)
        {
            StandAt(far, new Vec3(300, 0, 0));
        }

        return new Scene(world, talker, near, far);
    }

    private static void StandAt(FakePlayer player, Vec3 position)
        => player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId, new FusionRigPose { PelvisPosition = position }));

    /// <summary>A voice packet the way Fusion sends it: unreliable, to the other clients.</summary>
    private static byte[] Voice(FakePlayer sender, int bytes = 200)
    {
        var message = new FusionNetWriter(bytes + 16);
        message.Write(FusionProtocol.TagPlayerVoiceChat);
        message.Write((byte)3);
        message.Write((byte)1);
        message.WriteNullable(sender.SmallId);
        message.WriteBlock(new byte[bytes]);

        return message.ToArray();
    }

    private static int Heard(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection).Count(sent => sent.Message[0] == FusionProtocol.TagPlayerVoiceChat);

    private static byte[] ProxyInput(ushort entity, byte player, bool on)
        => new byte[] { 1, (byte)(entity >> 8), (byte)entity, 0, 2, 0, player, on ? (byte)1 : (byte)0 };

    [Fact]
    public void A_player_in_range_hears_and_one_out_of_range_does_not()
    {
        var scene = Build();
        using var world = scene.World;

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Near));
        Assert.Equal(0, Heard(world, scene.Far));
    }

    [Fact]
    public void Zero_range_sends_voice_to_everyone()
    {
        var scene = Build(new ServerConfig { CullOrphanedEntities = false, VoiceRelayRange = 0 });
        using var world = scene.World;

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void The_default_range_is_Fusions_audible_distance_with_a_margin()
        => Assert.Equal(41f, new ServerConfig().VoiceRelayRange);

    [Fact]
    public void A_player_whose_position_is_not_known_hears_everything()
    {
        var scene = Build(farHasPosition: false);
        using var world = scene.World;

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void A_loading_listener_hears_everything()
    {
        var scene = Build();
        using var world = scene.World;
        scene.Far.Send(ClientMessages.Metadata(scene.Far.SmallId, "Loading", "true"));

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void A_loading_talker_is_heard_by_everyone()
    {
        var scene = Build();
        using var world = scene.World;
        scene.Talker.Send(ClientMessages.Metadata(scene.Talker.SmallId, "Loading", "true"));

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void A_tall_avatar_carries_as_far_as_Fusion_plays_it()
    {
        var scene = Build();
        using var world = scene.World;
        byte[] stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "height", 1.76f * 9);
        world.Server.Players.Get(scene.Talker.SmallId)!.AvatarStats = stats;

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void A_talker_on_a_radio_or_phone_is_heard_everywhere_until_it_is_let_go()
    {
        var scene = Build();
        using var world = scene.World;

        world.Server.BroadcastModule(VoiceProxyInput, ProxyInput(500, scene.Talker.SmallId, on: true));
        world.Sync();
        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));

        world.Server.BroadcastModule(VoiceProxyInput, ProxyInput(500, scene.Talker.SmallId, on: false));
        world.Sync();
        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void A_proxy_handed_to_somebody_else_stops_carrying_the_first_talker()
    {
        var scene = Build();
        using var world = scene.World;

        world.Server.BroadcastModule(VoiceProxyInput, ProxyInput(500, scene.Talker.SmallId, on: true));
        world.Server.BroadcastModule(VoiceProxyInput, ProxyInput(500, scene.Near.SmallId, on: true));
        world.Sync();
        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(0, Heard(world, scene.Far));
    }

    [Fact]
    public void A_proxy_toggle_a_client_sends_counts_too()
    {
        var scene = Build();
        using var world = scene.World;

        scene.Near.Send(ClientMessages.ModuleToOthers(scene.Near.SmallId, VoiceProxyInput,
            ProxyInput(500, scene.Talker.SmallId, on: true)));
        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void A_muted_talker_is_still_heard_by_nobody()
    {
        var scene = Build();
        using var world = scene.World;
        world.Server.Mutes.Mute(76561198000000001);

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(0, Heard(world, scene.Near));
    }

    private static PluginEvents Hooked(World world)
    {
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        world.Server.Plugins = events;
        return events;
    }

    [Fact]
    public void A_plugin_can_keep_a_voice_from_one_listener()
    {
        var scene = Build();
        using var world = scene.World;
        var other = world.Join(76561198000000004, "Ellie");
        other.FinishLoading();
        StandAt(other, new Vec3(190, 0, 0));
        var events = Hooked(world);
        var asked = new List<VoiceEvent>();
        events.Voice.Subscribe("test", e =>
        {
            asked.Add(e);
            return e.ListenerPlatformId == 76561198000000002 ? PluginVerdict.Refuse("wall") : PluginVerdict.Allow;
        });

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(0, Heard(world, scene.Near));
        Assert.Equal(1, Heard(world, other));
        Assert.Equal(2, asked.Count);
        var e = Assert.Single(asked, a => a.ListenerPlatformId == 76561198000000002);
        Assert.Equal(200f, e.SpeakerX);
        Assert.Equal(210f, e.ListenerX);
        Assert.Equal(41f, e.Range);
    }

    [Fact]
    public void A_loading_listener_hears_whatever_a_plugin_says()
    {
        var scene = Build();
        using var world = scene.World;
        Hooked(world).Voice.Subscribe("test", _ => PluginVerdict.Refuse("wall"));
        scene.Near.Send(ClientMessages.Metadata(scene.Near.SmallId, "Loading", "true"));

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Near));
    }

    [Fact]
    public void A_loading_talker_is_heard_whatever_a_plugin_says()
    {
        var scene = Build();
        using var world = scene.World;
        Hooked(world).Voice.Subscribe("test", _ => PluginVerdict.Refuse("wall"));
        scene.Talker.Send(ClientMessages.Metadata(scene.Talker.SmallId, "Loading", "true"));

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Near));
    }

    [Fact]
    public void A_proxy_talker_never_reaches_the_hook()
    {
        var scene = Build();
        using var world = scene.World;
        Hooked(world).Voice.Subscribe("test", _ => PluginVerdict.Refuse("wall"));

        world.Server.BroadcastModule(VoiceProxyInput, ProxyInput(500, scene.Talker.SmallId, on: true));
        world.Sync();
        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Near));
        Assert.Equal(1, Heard(world, scene.Far));
    }

    [Fact]
    public void An_empty_voice_channel_changes_nothing()
    {
        var scene = Build();
        using var world = scene.World;
        Hooked(world);

        scene.Talker.Send(Voice(scene.Talker));

        Assert.Equal(1, Heard(world, scene.Near));
    }

    private static List<string> VoiceLines(World world)
        => world.Server.RecentLog(2000).Select(e => e.Message).Where(m => m.StartsWith("Voice by sender")).ToList();

    [Fact]
    public void An_open_mic_shows_up_in_the_minute_log()
    {
        var scene = Build();
        using var world = scene.World;
        world.Advance(TimeSpan.FromMinutes(1));
        world.Tick();

        for (var i = 0; i < 30; i++)
        {
            scene.Talker.Send(Voice(scene.Talker, 1000));
        }

        scene.Near.Send(Voice(scene.Near, 100));
        world.Advance(TimeSpan.FromMinutes(1));
        world.Tick();

        string line = Assert.Single(VoiceLines(world));
        Assert.Contains("Dennis 30 KB", line);
        Assert.DoesNotContain("Joel", line);
    }
}
