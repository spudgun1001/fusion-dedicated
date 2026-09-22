using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// The nightclub switches and the shop alarms against a real server and the real doors and club plugins, at the
/// live RPC budget of 60 and the shipped 250. They are plain level RPCs that only the server's cache carries.
/// Paths come from Southside.unity as built and baked on 2026-09-22.
/// </summary>
public class SouthsideShowTests
{
    private const ushort OnVariable = 0;
    private const ushort ColourVariable = 1;

    private const ulong Joel = 76561198000000001;
    private const ulong Dennis = 76561198000000002;
    private const ulong Tony = 76561198000000003;

    private const string Scene = "Southside";
    private const string Club = "Southside_Club";
    private const string Alarms = "Southside_Alarms";

    private static readonly Dictionary<string, string> ClubHosts = new()
    {
        ["Fog"] = LevelHash("Fog" + 0 + Scene + Club),
        ["Lasers"] = LevelHash("Lasers" + 1 + Scene + Club),
        ["Lights"] = LevelHash("Lights" + 2 + Scene + Club),
    };

    /// <summary>The shops in board order. Each shop's object sits at 5 + 2i and its alarm host right after it.</summary>
    private static readonly string[] Shops =
    {
        "712AB", "ElecAB", "ClothAB", "GunAB", "PawnAB", "DrugAB", "BankAB", "GasAB", "JwlAB", "ToolAB",
        "Vac1AB", "Vac2AB", "Vac3AB", "Vac4AB", "Vac5AB", "Vac6AB",
    };

    private static string Alarm(string button)
    {
        int index = 6 + 2 * Array.IndexOf(Shops, button);
        return LevelHash("AlarmRPC_" + button + index + Scene + Alarms);
    }

    private static IEnumerable<string> AllAlarms => Shops.Select(Alarm);

    /// <summary>Fusion's hierarchy hash: name, sibling index, scene name, then every parent's name.</summary>
    private static string LevelHash(string hierarchy)
    {
        unchecked
        {
            int a = 352654597, b = a;

            for (int i = 0; i < hierarchy.Length; i += 2)
            {
                a = ((a << 5) + a) ^ hierarchy[i];

                if (i == hierarchy.Length - 1)
                {
                    break;
                }

                b = ((b << 5) + b) ^ hierarchy[i + 1];
            }

            return LevelRpc.Hash((uint)(a + b * 1566083941));
        }
    }

    private static LevelRig Rig(int rpcBudget)
    {
        Assert.True(PluginSource.Repository != null,
            "The fusion-server-mods checkout was not found. Set FUSION_SERVER_MODS to it.");

        string[] plugins = { "doors", "club" };

        Assert.True(plugins.All(PluginSource.Has), $"Build the plugins first in '{PluginSource.Directory}'.");

        var rig = new LevelRig(new ServerConfig { CullOrphanedEntities = false, RpcMessagesPerSecond = rpcBudget }, plugins);

        Assert.Equal(plugins.Length, rig.Started);
        Assert.True(rig.Server.PluginRpc!.Watched, "the doors and club plugins should both be watching RPCs");

        return rig;
    }

    private static FakePlayer Arrive(LevelRig rig, ulong platformId, string name)
    {
        var player = rig.World.Join(platformId, name);
        player.FinishLoading();
        player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId,
            new FusionRigPose { PelvisPosition = new Vec3(4f, 0f, 9f) }));

        return player;
    }

    /// <summary>Joins after the fact and lets the paced catch-up run out, a second at a time.</summary>
    private static FakePlayer ArriveLate(LevelRig rig, ulong platformId, string name)
    {
        var player = Arrive(rig, platformId, name);

        for (var i = 0; i < 10; i++)
        {
            rig.World.Advance(TimeSpan.FromSeconds(1));
        }

        return player;
    }

    private static List<bool> Bools(LevelRig rig, FakePlayer player, string hash, ushort index = OnVariable)
        => LevelRpc.Heard(rig.World, player, hash, index, RpcKind.Bool).Select(v => v.Bool).ToList();

    private static List<int> Ints(LevelRig rig, FakePlayer player, string hash, ushort index)
        => LevelRpc.Heard(rig.World, player, hash, index, RpcKind.Int).Select(v => v.Int).ToList();

    [Fact]
    public void The_paths_match_the_hashes_read_off_the_built_scene()
    {
        Assert.Equal(LevelRpc.Hash(0x8AEF9465), ClubHosts["Fog"]);
        Assert.Equal(LevelRpc.Hash(0xDF5F6A02), ClubHosts["Lasers"]);
        Assert.Equal(LevelRpc.Hash(0xEEAC2358), ClubHosts["Lights"]);
        Assert.Equal(LevelRpc.Hash(0x62EBE937), Alarm("712AB"));
        Assert.Equal(LevelRpc.Hash(0x58B7A9EE), Alarm("GunAB"));
        Assert.Equal(LevelRpc.Hash(0x4CF75DDC), Alarm("Vac6AB"));
        Assert.Equal(16, AllAlarms.Distinct().Count());
    }

    [Theory]
    [InlineData(60, "Fog")]
    [InlineData(250, "Fog")]
    [InlineData(60, "Lasers")]
    [InlineData(250, "Lasers")]
    [InlineData(60, "Lights")]
    [InlineData(250, "Lights")]
    public void A_club_switch_reaches_another_player_and_a_late_joiner_untouched(int budget, string switchName)
    {
        string host = ClubHosts[switchName];

        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        joel.Send(LevelRpc.Bool(joel.SmallId, host, OnVariable, true));

        Assert.Equal(new[] { true }, Bools(rig, dennis, host));
        Assert.Empty(Bools(rig, joel, host));

        var tony = ArriveLate(rig, Tony, "Tony");
        Assert.Equal(new[] { true }, Bools(rig, tony, host));

        joel.Send(LevelRpc.Bool(joel.SmallId, host, OnVariable, false));

        Assert.Equal(new[] { true, false }, Bools(rig, dennis, host));
        Assert.Equal(new[] { true, false }, Bools(rig, tony, host));
        Assert.Empty(Bools(rig, joel, host));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_laser_colour_reaches_another_player_and_a_late_joiner_untouched(int budget)
    {
        string lasers = ClubHosts["Lasers"];

        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        joel.Send(LevelRpc.Int(joel.SmallId, lasers, ColourVariable, 1));
        joel.Send(LevelRpc.Int(joel.SmallId, lasers, ColourVariable, 2));

        Assert.Equal(new[] { 1, 2 }, Ints(rig, dennis, lasers, ColourVariable));
        Assert.Empty(Ints(rig, joel, lasers, ColourVariable));

        var tony = ArriveLate(rig, Tony, "Tony");

        Assert.Equal(new[] { 2 }, Ints(rig, tony, lasers, ColourVariable));
        Assert.Empty(Bools(rig, tony, lasers));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void One_shop_alarm_reaches_everybody_and_leaves_the_other_shops_alone(int budget)
    {
        string gunStore = Alarm("GunAB");

        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        joel.Send(LevelRpc.Bool(joel.SmallId, gunStore, OnVariable, true));

        var tony = ArriveLate(rig, Tony, "Tony");

        Assert.Equal(new[] { true }, Bools(rig, dennis, gunStore));
        Assert.Equal(new[] { true }, Bools(rig, tony, gunStore));
        Assert.Empty(Bools(rig, joel, gunStore));

        foreach (string other in AllAlarms.Where(a => a != gunStore))
        {
            Assert.Empty(Bools(rig, joel, other));
            Assert.Empty(Bools(rig, dennis, other));
            Assert.Empty(Bools(rig, tony, other));
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_clear_plate_turns_every_alarm_off_for_everybody(int budget)
    {
        string[] ringing = { Alarm("712AB"), Alarm("BankAB"), Alarm("Vac6AB") };

        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        foreach (string alarm in ringing)
        {
            dennis.Send(LevelRpc.Bool(dennis.SmallId, alarm, OnVariable, true));
        }

        rig.World.Advance(TimeSpan.FromSeconds(1));

        // The press writes all sixteen in one frame, and Fusion's grip relay makes every other game do the same.
        joel.SendMany(AllAlarms.Select(a => LevelRpc.Bool(joel.SmallId, a, OnVariable, false)));
        dennis.SendMany(AllAlarms.Select(a => LevelRpc.Bool(dennis.SmallId, a, OnVariable, false)));

        var tony = ArriveLate(rig, Tony, "Tony");

        foreach (string alarm in AllAlarms)
        {
            Assert.False(Bools(rig, dennis, alarm).Last(), "Dennis " + alarm);
            Assert.False(Bools(rig, joel, alarm).Last(), "Joel " + alarm);
            Assert.Equal(new[] { false }, Bools(rig, tony, alarm));
        }

        foreach (string alarm in ringing)
        {
            Assert.Equal(new[] { true, false }, Bools(rig, joel, alarm));
            Assert.Equal(new[] { false }, Bools(rig, dennis, alarm));
        }
    }
}
