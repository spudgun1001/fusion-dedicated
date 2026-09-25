using System.Reflection;
using System.Text.Json;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// The Southside level against a real server and the real plugins, at the live RPC budget of 60 and the shipped 250.
/// Paths come from Southside.unity as built on 2026-09-22, and the phones are synthetic until the owner places some.
/// </summary>
[Trait("Needs", "fusion-server-mods")]
public class SouthsideLevelTests
{
    // Wire indices: Fusion registers a level host's components in the reverse of the builder's order.
    private const int LevelAnnounce = 1001;
    private const ushort HandshakeVariable = 1;
    private const ushort AnswerVariable = 0;

    private const ushort ToggleEvent = 14;
    private const ushort OpenMenuEvent = 12;
    private const ushort LockedVariable = 27;
    private const ushort MenuShownVariable = 23;
    private const ushort TitleVariable = 22;

    private const ushort DigitVariable = 8;
    private const ushort MyNumberVariable = 7;
    private const ushort KindVariable = 6;
    private const ushort DisplayVariable = 5;
    private const ushort ChannelVariable = 4;
    private const ushort InCallVariable = 3;
    private const ushort RingMutedVariable = 2;
    private const ushort ToneMutedVariable = 1;
    private const ushort RingbackMutedVariable = 0;
    private const ushort HangUpEvent = 7;
    private const ushort CallEvent = 5;
    private const ushort LiftEvent = 4;
    private const int LevelKind = 0;

    /// <summary>The most phones one load can announce at the live budget: the handshake takes the 60th message.</summary>
    private const int SyntheticPhones = 59;

    private const ulong Joel = 76561198000000001;
    private const ulong Dennis = 76561198000000002;
    private const ulong Tony = 76561198000000003;

    private const string Scene = "Southside";
    private const string Slots = "Southside_DoorSlots";

    private static readonly string Handshake = LevelHash("Southside_PluginHandshake" + 13 + Scene);

    /// <summary>An apartment door the classifier calls Owned.</summary>
    private static readonly string Door = LevelHash("DoorRPC_AptB1D3" + 45 + Scene + Slots);

    /// <summary>Southside has no door named as a vent, so a real owned door stands in for one.</summary>
    private static readonly string Vent = LevelHash("DoorRPC_AptB2D8" + 25 + Scene + Slots);

    /// <summary>
    /// The sewer manhole Manhole4, which the classifier calls a Mover. The current build still gives it this
    /// RPC host; the mover builder drops the host, so the level never says anything on this path.
    /// </summary>
    private static readonly string Mover = LevelHash("DoorRPC_Manhole4" + 1107 + Scene + Slots);

    private static string PhoneAt(int i) => LevelHash("Southside_Payphone" + i + Scene + "Southside_Phones");

    /// <summary>A level component's hash: Fusion hashes name, sibling index, scene name, then every parent's name.</summary>
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

    private static LevelRig Rig(int rpcBudget, IReadOnlyDictionary<string, string>? saved = null, bool gangs = false)
    {
        Assert.True(PluginSource.Repository != null,
            "The fusion-server-mods checkout was not found. Set FUSION_SERVER_MODS to it.");

        string[] plugins = gangs ? new[] { "doors", "gangs", "phones" } : new[] { "doors", "phones" };

        Assert.True(plugins.All(PluginSource.Has), $"Build the plugins first in '{PluginSource.Directory}'.");

        var rig = new LevelRig(new ServerConfig { CullOrphanedEntities = false, RpcMessagesPerSecond = rpcBudget },
            plugins, saved);

        Assert.Equal(plugins.Length, rig.Started);

        return rig;
    }

    private static FakePlayer Arrive(LevelRig rig, ulong platformId, string name)
    {
        var player = rig.World.Join(platformId, name);
        player.FinishLoading();
        Stand(player);

        return player;
    }

    private static void Stand(FakePlayer player)
        => player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId,
            new FusionRigPose { PelvisPosition = new Vec3(4f, 0f, 9f) }));

    /// <summary>What the level writes in the frame it starts: the handshake, then each phone's kind.</summary>
    private static List<byte[]> LevelLoad(FakePlayer player, int phones)
    {
        var messages = new List<byte[]> { LevelRpc.Int(player.SmallId, Handshake, HandshakeVariable, LevelAnnounce) };

        for (var i = 0; i < phones; i++)
        {
            messages.Add(LevelRpc.Int(player.SmallId, PhoneAt(i), KindVariable, LevelKind));
        }

        return messages;
    }

    /// <summary>Loads the level, then lets a second pass, since nobody presses anything in the frame it loads.</summary>
    private static void LoadLevel(LevelRig rig, FakePlayer player, int phones = SyntheticPhones)
    {
        player.SendMany(LevelLoad(player, phones));
        rig.World.Advance(TimeSpan.FromSeconds(1));
    }

    /// <summary>Lets the paced join catch-up run out, a second at a time as the server's loop would.</summary>
    private static void Settle(LevelRig rig)
    {
        for (var i = 0; i < 10; i++)
        {
            rig.World.Advance(TimeSpan.FromSeconds(1));
        }
    }

    private static int PhonesAdopted(LevelRig rig) => rig.LinesSaying("A phone built into the level answers on");

    private static string LastString(LevelRig rig, FakePlayer player, string hash, ushort index)
    {
        var heard = LevelRpc.Heard(rig.World, player, hash, index, RpcKind.String);
        return heard.Count == 0 ? "" : heard[^1].Text;
    }

    private static bool? LastBool(LevelRig rig, FakePlayer player, string hash, ushort index)
    {
        var heard = LevelRpc.Heard(rig.World, player, hash, index, RpcKind.Bool);
        return heard.Count == 0 ? null : heard[^1].Bool;
    }

    private static void SeeDoor(FakePlayer player, string door)
    {
        player.Send(LevelRpc.Int(player.SmallId, Handshake, HandshakeVariable, LevelAnnounce));
        player.Send(LevelRpc.Event(player.SmallId, door, OpenMenuEvent));
    }

    /// <summary>One door already on the book, at the spot the plugin saves a level door under.</summary>
    private static Dictionary<string, string> Book(string door, string[] owners, bool locked = false)
        => new()
        {
            ["doors"] = JsonSerializer.Serialize(new
            {
                doors = new
                {
                    NextId = 2,
                    Doors = new Dictionary<string, object>
                    {
                        ["D1"] = new
                        {
                            Id = "D1",
                            Spot = "level@" + LevelRpc.Path(door, 0),
                            X = 4f,
                            Y = 0f,
                            Z = 9f,
                            Owners = owners,
                            Locked = locked,
                        },
                    },
                },
            }),
        };

    /// <summary>The panel the owner sets a gang from. LevelRig keeps it private, so it is read by reflection.</summary>
    private static PluginPanel Panel(LevelRig rig)
    {
        object host = typeof(LevelRig).GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(rig)!;
        return (PluginPanel)host.GetType().GetField("_panel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
    }

    private static int DoorsOnRecord(LevelRig rig)
        => Panel(rig).Build("doors")!.Sections.Single(s => s.Title == "Doors").Rows.Count;

    // ---- 1. the handshake ----

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_plugin_answers_the_levels_handshake(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var answers = LevelRpc.Heard(rig.World, joel, Handshake, AnswerVariable, RpcKind.Bool);

        Assert.NotEmpty(answers);
        Assert.All(answers, a => Assert.True(a.Bool));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_announce_itself_is_never_relayed_to_anybody_else(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        Assert.Empty(LevelRpc.Heard(rig.World, dennis, Handshake, HandshakeVariable, RpcKind.Int));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_late_joiner_is_told_the_answer_from_the_cache_before_their_level_says_anything(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var dennis = Arrive(rig, Dennis, "Dennis");
        var answers = LevelRpc.Heard(rig.World, dennis, Handshake, AnswerVariable, RpcKind.Bool);

        Assert.NotEmpty(answers);
        Assert.All(answers, a => Assert.True(a.Bool));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_late_joiners_own_copy_cannot_flip_everybody_back_to_hand_mode(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var dennis = Arrive(rig, Dennis, "Dennis");
        dennis.Send(LevelRpc.Bool(dennis.SmallId, Handshake, AnswerVariable, false));

        Assert.All(LevelRpc.Heard(rig.World, joel, Handshake, AnswerVariable, RpcKind.Bool), a => Assert.True(a.Bool));
        Assert.All(LevelRpc.Heard(rig.World, dennis, Handshake, AnswerVariable, RpcKind.Bool), a => Assert.True(a.Bool));
    }

    // ---- 2. an owned door ----

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_menu_a_grip_opens_adopts_the_door_and_comes_back_to_the_opener_only(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        SeeDoor(joel, Door);

        Assert.True(LastBool(rig, joel, Door, MenuShownVariable));
        Assert.NotEqual("", LastString(rig, joel, Door, TitleVariable));
        Assert.DoesNotContain(LevelRpc.Heard(rig.World, dennis, Door, MenuShownVariable, RpcKind.Bool), v => v.Bool);
        Assert.Equal(1, DoorsOnRecord(rig));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_knob_locks_an_owned_door_and_the_lock_reaches_another_player_and_a_late_joiner(int budget)
    {
        using var rig = Rig(budget, Book(Door, new[] { Joel.ToString() }));
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        SeeDoor(joel, Door);
        joel.Send(LevelRpc.Event(joel.SmallId, Door, ToggleEvent));

        var tony = Arrive(rig, Tony, "Tony");

        Assert.True(LastBool(rig, joel, Door, LockedVariable));
        Assert.True(LastBool(rig, dennis, Door, LockedVariable));
        Assert.True(LastBool(rig, tony, Door, LockedVariable));
        Assert.Equal(1, rig.LinesSaying("Joel locked"));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_locked_door_is_announced_after_a_restart_before_anybody_opens_its_menu(int budget)
    {
        using var rig = Rig(budget, Book(Door, new[] { Joel.ToString() }, locked: true));
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));
        rig.World.Advance(TimeSpan.FromSeconds(30));
        rig.World.Tick();

        Assert.True(LastBool(rig, joel, Door, LockedVariable));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_player_with_no_key_cannot_lock_the_door(int budget)
    {
        using var rig = Rig(budget, Book(Door, new[] { Joel.ToString() }));
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        SeeDoor(joel, Door);
        dennis.Send(LevelRpc.Event(dennis.SmallId, Door, ToggleEvent));

        Assert.DoesNotContain(LevelRpc.Heard(rig.World, joel, Door, LockedVariable, RpcKind.Bool), v => v.Bool);
        Assert.Equal(1, rig.LinesSaying("Dennis has no key"));
    }

    // ---- 3. a gang locks a vent ----

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_gang_member_locks_a_vent_the_panel_gave_their_gang_and_everybody_sees_it(int budget)
    {
        var saved = Book(Vent, Array.Empty<string>());
        saved["gangs"] = JsonSerializer.Serialize(new
        {
            register = new { Gangs = new[] { new { Id = "g1", Name = "Ballas", Members = new[] { Joel.ToString() } } } },
        });

        using var rig = Rig(budget, saved, gangs: true);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        // The panel's "Set a gang" button, which is DoorBook.SetGang.
        Assert.True(Panel(rig).Invoke("doors", "gang",
            new Dictionary<string, string> { ["door"] = "D1", ["gang"] = "g1" }).Handled);

        // Joel holds no key of his own, so only the gang lets him lock it.
        SeeDoor(joel, Vent);
        joel.Send(LevelRpc.Event(joel.SmallId, Vent, ToggleEvent));

        var tony = Arrive(rig, Tony, "Tony");

        Assert.Equal(1, rig.LinesSaying("Joel locked"));
        Assert.True(LastBool(rig, joel, Vent, LockedVariable));
        Assert.True(LastBool(rig, dennis, Vent, LockedVariable));
        Assert.True(LastBool(rig, tony, Vent, LockedVariable));
    }

    // ---- 4. a mover is never adopted ----

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_mover_is_never_adopted_and_never_recorded(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(rig, joel);

        // A mover has no RPC stack. Even event 0, which buttons and lifts also fire, adopts nothing.
        joel.Send(LevelRpc.Event(joel.SmallId, Mover, ToggleEvent));
        rig.World.Advance(TimeSpan.FromSeconds(30));
        rig.World.Tick();

        Assert.Empty(LevelRpc.Heard(rig.World, joel, Mover, LockedVariable, RpcKind.Bool));
        Assert.Empty(LevelRpc.Heard(rig.World, joel, Mover, MenuShownVariable, RpcKind.Bool));
        Assert.Equal(0, DoorsOnRecord(rig));
    }

    // ---- 5. phones (synthetic, see the class summary) ----

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void Every_phone_in_the_level_is_adopted_with_a_number_of_its_own(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(rig, joel);

        var numbers = Enumerable.Range(0, SyntheticPhones)
            .Select(i => LastString(rig, joel, PhoneAt(i), MyNumberVariable)).ToList();

        Assert.Equal(SyntheticPhones, PhonesAdopted(rig));
        Assert.All(numbers, n => Assert.Matches("^[0-9]+$", n));
        Assert.Equal(SyntheticPhones, numbers.Distinct().Count());
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_phone_the_level_announces_while_still_loading_is_numbered_once_loading_ends(int budget)
    {
        // A client drops an RPC int or string while it loads, and a level writes its phones from Start.
        using var rig = Rig(budget);
        var joel = rig.World.Join(Joel, "Joel");

        joel.SendMany(LevelLoad(joel, SyntheticPhones));

        int before = LevelRpc.Heard(rig.World, joel, PhoneAt(0), MyNumberVariable, RpcKind.String).Count;

        joel.FinishLoading();
        Settle(rig);

        for (var i = 0; i < SyntheticPhones; i++)
        {
            var after = LevelRpc.Heard(rig.World, joel, PhoneAt(i), MyNumberVariable, RpcKind.String).Skip(before).ToList();
            Assert.True(after.Count > 0, $"phone {i} was never numbered after loading");
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_late_joiner_sees_every_phone_numbered_and_silent(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(rig, joel);

        var dennis = rig.World.Join(Dennis, "Dennis");
        dennis.SendMany(LevelLoad(dennis, SyntheticPhones));
        dennis.FinishLoading();
        Settle(rig);

        for (var i = 0; i < SyntheticPhones; i++)
        {
            Assert.Equal(LastString(rig, joel, PhoneAt(i), MyNumberVariable), LastString(rig, dennis, PhoneAt(i), MyNumberVariable));
            Assert.True(LastBool(rig, dennis, PhoneAt(i), RingMutedVariable), $"phone {i} ring");
        }
    }

    [Theory]
    [InlineData(60, SyntheticPhones - 1)]
    [InlineData(250, SyntheticPhones)]
    public void A_joiners_own_answer_write_costs_the_level_a_phone_at_the_live_budget(int budget, int adopted)
    {
        // The handshake and 59 phones are 60 messages; the answer bool a joining copy writes is the 61st.
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        var load = LevelLoad(joel, SyntheticPhones);
        load.Insert(1, LevelRpc.Bool(joel.SmallId, Handshake, AnswerVariable, false));
        joel.SendMany(load);

        rig.World.Advance(TimeSpan.FromMinutes(1));
        rig.World.Tick();

        Assert.Equal(adopted, PhonesAdopted(rig));
        Assert.Equal(budget == 60 ? 1 : 0, rig.LinesSaying("Dropped 1 RPC messages from Joel in the last minute"));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void Digits_show_as_they_are_dialled(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(rig, joel);

        Assert.Equal("---", LastString(rig, joel, PhoneAt(0), DisplayVariable));

        joel.Send(LevelRpc.Int(joel.SmallId, PhoneAt(0), DigitVariable, 4));
        Assert.Equal("4", LastString(rig, joel, PhoneAt(0), DisplayVariable));

        joel.Send(LevelRpc.Int(joel.SmallId, PhoneAt(0), DigitVariable, 1));
        Assert.Equal("41", LastString(rig, joel, PhoneAt(0), DisplayVariable));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_call_connects_and_hanging_up_ends_it_on_both_phones(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        LoadLevel(rig, joel);

        string a = PhoneAt(0), b = PhoneAt(SyntheticPhones - 1);
        string numberA = LastString(rig, joel, a, MyNumberVariable);
        string numberB = LastString(rig, joel, b, MyNumberVariable);

        foreach (char digit in numberB)
        {
            joel.Send(LevelRpc.Int(joel.SmallId, a, DigitVariable, digit - '0'));
        }

        joel.Send(LevelRpc.Event(joel.SmallId, a, CallEvent));

        Assert.False(LastBool(rig, dennis, b, RingMutedVariable));
        Assert.False(LastBool(rig, joel, a, RingbackMutedVariable));
        Assert.Equal($"{numberA} calling", LastString(rig, dennis, b, DisplayVariable));

        dennis.Send(LevelRpc.Event(dennis.SmallId, b, LiftEvent));

        Assert.True(LastBool(rig, joel, a, InCallVariable));
        Assert.True(LastBool(rig, dennis, b, InCallVariable));
        Assert.Equal(LastString(rig, joel, a, ChannelVariable), LastString(rig, dennis, b, ChannelVariable));

        joel.Send(LevelRpc.Event(joel.SmallId, a, HangUpEvent));

        Assert.False(LastBool(rig, joel, a, InCallVariable));
        Assert.False(LastBool(rig, dennis, b, InCallVariable));
        Assert.Equal(numberA, LastString(rig, dennis, a, ChannelVariable));
        Assert.Equal(numberB, LastString(rig, dennis, b, ChannelVariable));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void An_idle_phone_is_muted_on_all_three_loops(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(rig, joel);

        for (var i = 0; i < SyntheticPhones; i++)
        {
            Assert.True(LastBool(rig, joel, PhoneAt(i), RingMutedVariable), $"phone {i} ring");
            Assert.True(LastBool(rig, joel, PhoneAt(i), ToneMutedVariable), $"phone {i} tone");
            Assert.True(LastBool(rig, joel, PhoneAt(i), RingbackMutedVariable), $"phone {i} ringback");
        }
    }
}
