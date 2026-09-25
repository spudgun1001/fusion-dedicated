using System.Text.Json;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// The whole chain the EvoCity level talks to: a real server, the real doors and
/// phones plugins off disk, and clients that send what the level sends.
///
/// Run this before packing a level build. Nothing here mocks the server or the
/// plugins, so a pass means the messages the level sends really did reach them.
/// </summary>
[Trait("Needs", "fusion-server-mods")]
public class EvoCityLevelTests
{
    // Wire indices: Fusion registers a level host's components in the reverse of the builder's order.
    // What the level writes on its handshake object, and the two components on it.
    private const int LevelAnnounce = 1001;
    private const ushort HandshakeVariable = 1;
    private const ushort AnswerVariable = 0;

    // What a phone built into the level writes to say it is a phone.
    private const ushort KindVariable = 6;
    private const ushort MyNumberVariable = 7;
    private const int LevelKind = 0;

    // The door components the menu chain uses.
    private const ushort ToggleEvent = 14;
    private const ushort OpenMenuEvent = 12;
    private const ushort LockedVariable = 27;
    private const ushort MenuShownVariable = 23;
    private const ushort TitleVariable = 22;

    /// <summary>How many phones the EvoCity build puts in the level.</summary>
    private const int LevelPhones = 59;

    /// <summary>What the owner's live server.json allows one player a second.</summary>
    private const int LiveRpcBudget = 60;

    private const ulong Joel = 76561198000000001;
    private const ulong Dennis = 76561198000000002;

    private static readonly string Handshake = LevelRpc.Hash(0x73F1A204);
    private static readonly string Door = LevelRpc.Hash(0x8C22B001);

    private static string PhoneAt(int i) => LevelRpc.Hash((uint)(0x1000 + i));

    private static string Name(string loaded) => loaded.Split(' ')[0];

    private static ServerConfig Config(int rpcBudget) => new()
    {
        CullOrphanedEntities = false,
        RpcMessagesPerSecond = rpcBudget,
    };

    private static LevelRig Rig(int rpcBudget = 250, IReadOnlyDictionary<string, string>? saved = null)
    {
        Assert.True(PluginSource.Repository != null,
            "The fusion-server-mods checkout was not found. Set FUSION_SERVER_MODS to it.");

        Assert.True(PluginSource.Has("doors") && PluginSource.Has("phones"),
            $"Build the plugins first: no doors or phones in '{PluginSource.Directory}'.");

        var rig = new LevelRig(Config(rpcBudget), new[] { "doors", "phones" }, saved);

        // Named rather than versioned, so a plugin release does not have to edit the test.
        Assert.Equal(2, rig.Started);
        Assert.Equal(new[] { "doors", "phones" }, rig.Loaded.Select(Name).OrderBy(n => n).ToArray());

        return rig;
    }

    /// <summary>A player who has finished loading and whose game has said where they stand.</summary>
    private static FakePlayer Arrive(LevelRig rig, ulong platformId, string name, float x = 4f, float z = 9f)
    {
        var player = rig.World.Join(platformId, name);
        player.FinishLoading();
        player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId,
            new FusionRigPose { PelvisPosition = new Vec3(x, 0f, z) }));

        return player;
    }

    /// <summary>Everything the level writes as it loads, in the one frame it writes it.</summary>
    private static void LoadLevel(FakePlayer player, int phones = LevelPhones)
    {
        var messages = new List<byte[]>
        {
            LevelRpc.Int(player.SmallId, Handshake, HandshakeVariable, LevelAnnounce),
        };

        for (var i = 0; i < phones; i++)
        {
            messages.Add(LevelRpc.Int(player.SmallId, PhoneAt(i), KindVariable, LevelKind));
        }

        player.SendMany(messages);
    }

    private static int PhonesAdopted(LevelRig rig)
        => rig.LinesSaying("A phone built into the level answers on");

    // ---- 1. the handshake ----

    [Fact]
    public void The_plugin_answers_the_levels_handshake()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var answers = LevelRpc.Heard(rig.World, joel, Handshake, AnswerVariable, RpcKind.Bool);

        Assert.NotEmpty(answers);
        Assert.All(answers, a => Assert.True(a.Bool));
    }

    [Fact]
    public void The_announce_itself_is_never_relayed_to_anybody_else()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        Assert.Empty(LevelRpc.Heard(rig.World, dennis, Handshake, HandshakeVariable, RpcKind.Int));
    }

    [Fact]
    public void A_late_joiner_is_told_the_plugin_is_there()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var dennis = Arrive(rig, Dennis, "Dennis");
        dennis.Send(LevelRpc.Int(dennis.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var answers = LevelRpc.Heard(rig.World, dennis, Handshake, AnswerVariable, RpcKind.Bool);

        Assert.NotEmpty(answers);
        Assert.True(answers[^1].Bool);
    }

    [Fact]
    public void A_late_joiner_is_told_by_the_catch_up_before_their_own_level_says_anything()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var dennis = Arrive(rig, Dennis, "Dennis");

        var answers = LevelRpc.Heard(rig.World, dennis, Handshake, AnswerVariable, RpcKind.Bool);

        Assert.NotEmpty(answers);
        Assert.All(answers, a => Assert.True(a.Bool));
    }

    [Fact]
    public void A_late_joiners_own_copy_cannot_flip_everybody_back_to_hand_mode()
    {
        // The bug this guards: a joining client's catch-up sends the answer it holds,
        // which is false, and everybody's level went back to hand mode.
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));

        var dennis = Arrive(rig, Dennis, "Dennis");
        dennis.Send(LevelRpc.Bool(dennis.SmallId, Handshake, AnswerVariable, false));

        Assert.All(LevelRpc.Heard(rig.World, joel, Handshake, AnswerVariable, RpcKind.Bool),
            a => Assert.True(a.Bool));

        Assert.All(LevelRpc.Heard(rig.World, dennis, Handshake, AnswerVariable, RpcKind.Bool),
            a => Assert.True(a.Bool));
    }

    // ---- 2. phones connect ----

    [Fact]
    public void Every_phone_in_the_level_is_adopted_and_given_a_number()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(joel);

        Assert.Equal(LevelPhones, PhonesAdopted(rig));

        // A number on the phone itself, not only a line in the log.
        var numbers = LevelRpc.Heard(rig.World, joel, PhoneAt(0), MyNumberVariable, RpcKind.String);

        Assert.NotEmpty(numbers);
        Assert.Matches("^[0-9]+$", numbers[^1].Text);
    }

    [Fact]
    public void Every_phone_gets_a_number_of_its_own()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(joel);

        var numbers = new List<string>();

        for (var i = 0; i < LevelPhones; i++)
        {
            var heard = LevelRpc.Heard(rig.World, joel, PhoneAt(i), MyNumberVariable, RpcKind.String);
            numbers.Add(heard.Count == 0 ? "" : heard[^1].Text);
        }

        Assert.DoesNotContain("", numbers);
        Assert.Equal(LevelPhones, numbers.Distinct().Count());
    }

    [Fact]
    public void The_levels_whole_load_fits_the_live_rpc_budget_with_nothing_to_spare()
    {
        using var rig = Rig(LiveRpcBudget);
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(joel);

        Assert.Equal(LevelPhones, PhonesAdopted(rig));
    }

    [Fact]
    public void One_more_message_in_the_same_second_is_eaten_before_any_plugin_sees_it()
    {
        // The budget is spent by the handshake and the 59 phones, so the next thing the
        // level says that second is dropped at FusionServer.cs and leaves no trace.
        using var rig = Rig(LiveRpcBudget);
        var joel = Arrive(rig, Joel, "Joel");

        LoadLevel(joel);

        joel.Send(LevelRpc.Int(joel.SmallId, PhoneAt(LevelPhones), KindVariable, LevelKind));

        Assert.Equal(LevelPhones, PhonesAdopted(rig));

        // What the owner can grep for afterwards, once the minute is up.
        rig.World.Advance(TimeSpan.FromMinutes(1));
        rig.World.Tick();

        Assert.Equal(1, rig.LinesSaying("Dropped 1 RPC messages from Joel in the last minute"));
    }

    /// <summary>What a joining client sends: the announce, the answer its own copy holds, then every phone.</summary>
    private static void JoinLoad(FakePlayer player)
    {
        var messages = new List<byte[]>
        {
            LevelRpc.Int(player.SmallId, Handshake, HandshakeVariable, LevelAnnounce),
            LevelRpc.Bool(player.SmallId, Handshake, AnswerVariable, false),
        };

        for (var i = 0; i < LevelPhones; i++)
        {
            messages.Add(LevelRpc.Int(player.SmallId, PhoneAt(i), KindVariable, LevelKind));
        }

        player.SendMany(messages);
    }

    [Fact]
    public void A_joiners_own_catch_up_write_costs_the_level_a_phone_at_the_live_budget()
    {
        using var rig = Rig(LiveRpcBudget);
        var joel = Arrive(rig, Joel, "Joel");

        JoinLoad(joel);

        Assert.Equal(LevelPhones - 1, PhonesAdopted(rig));
    }

    [Fact]
    public void The_same_load_loses_nothing_at_the_shipped_default()
    {
        using var rig = Rig(250);
        var joel = Arrive(rig, Joel, "Joel");

        JoinLoad(joel);

        Assert.Equal(LevelPhones, PhonesAdopted(rig));
    }

    // ---- 3. the door menu ----

    [Fact]
    public void The_menu_a_grip_opens_comes_back_from_the_plugin()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));
        joel.Send(LevelRpc.Event(joel.SmallId, Door, OpenMenuEvent));

        var shown = LevelRpc.Heard(rig.World, joel, Door, MenuShownVariable, RpcKind.Bool);
        var title = LevelRpc.Heard(rig.World, joel, Door, TitleVariable, RpcKind.String);

        Assert.NotEmpty(shown);
        Assert.True(shown[^1].Bool);
        Assert.NotEmpty(title);
        Assert.NotEqual("", title[^1].Text);
    }

    [Fact]
    public void A_menu_is_shown_to_the_player_who_opened_it_and_to_nobody_else()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));
        joel.Send(LevelRpc.Event(joel.SmallId, Door, OpenMenuEvent));

        Assert.DoesNotContain(LevelRpc.Heard(rig.World, dennis, Door, MenuShownVariable, RpcKind.Bool),
            v => v.Bool);
    }

    // ---- 4. locking ----

    /// <summary>A door somebody already bought, saved where the level puts it.</summary>
    private static Dictionary<string, string> OwnedDoor(bool locked = false)
    {
        var book = new
        {
            doors = new
            {
                NextId = 2,
                Doors = new Dictionary<string, object>
                {
                    ["D1"] = new
                    {
                        Id = "D1",
                        Spot = "level@" + LevelRpc.Path(Door, 0),
                        X = 4f,
                        Y = 0f,
                        Z = 9f,
                        Name = "Police front",
                        Owners = new[] { Joel.ToString() },
                        Locked = locked,
                    },
                },
            },
        };

        return new Dictionary<string, string> { ["doors"] = JsonSerializer.Serialize(book) };
    }

    /// <summary>Opens the door's menu, which is what puts a level door in front of the plugin.</summary>
    private static void SeeDoor(LevelRig rig, FakePlayer player)
    {
        player.Send(LevelRpc.Int(player.SmallId, Handshake, HandshakeVariable, LevelAnnounce));
        player.Send(LevelRpc.Event(player.SmallId, Door, OpenMenuEvent));
    }

    [Fact]
    public void The_knob_locks_a_door_its_owner_is_holding()
    {
        using var rig = Rig(saved: OwnedDoor());
        var joel = Arrive(rig, Joel, "Joel");

        SeeDoor(rig, joel);
        joel.Send(LevelRpc.Event(joel.SmallId, Door, ToggleEvent));

        var locks = LevelRpc.Heard(rig.World, joel, Door, LockedVariable, RpcKind.Bool);

        Assert.NotEmpty(locks);
        Assert.True(locks[^1].Bool);
        Assert.Equal(1, rig.LinesSaying("Joel locked"));
    }

    [Fact]
    public void A_lock_reaches_the_other_players_too()
    {
        using var rig = Rig(saved: OwnedDoor());
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        SeeDoor(rig, joel);
        joel.Send(LevelRpc.Event(joel.SmallId, Door, ToggleEvent));

        var locks = LevelRpc.Heard(rig.World, dennis, Door, LockedVariable, RpcKind.Bool);

        Assert.NotEmpty(locks);
        Assert.True(locks[^1].Bool);
    }

    [Fact]
    public void A_player_who_joins_after_the_lock_is_told_it_is_locked()
    {
        using var rig = Rig(saved: OwnedDoor());
        var joel = Arrive(rig, Joel, "Joel");

        SeeDoor(rig, joel);
        joel.Send(LevelRpc.Event(joel.SmallId, Door, ToggleEvent));

        var dennis = Arrive(rig, Dennis, "Dennis");

        var locks = LevelRpc.Heard(rig.World, dennis, Door, LockedVariable, RpcKind.Bool);

        Assert.NotEmpty(locks);
        Assert.True(locks[^1].Bool);
    }

    [Fact]
    public void A_locked_level_door_is_announced_after_a_restart_before_anybody_opens_its_menu()
    {
        // A level door only reaches the plugin's list when somebody opens its menu, so
        // this is what a restart looks like: the book says locked and nobody has been near it.
        using var rig = Rig(saved: OwnedDoor(locked: true));
        var joel = Arrive(rig, Joel, "Joel");

        joel.Send(LevelRpc.Int(joel.SmallId, Handshake, HandshakeVariable, LevelAnnounce));
        rig.World.Advance(TimeSpan.FromSeconds(30));
        rig.World.Tick();

        var locks = LevelRpc.Heard(rig.World, joel, Door, LockedVariable, RpcKind.Bool);

        Assert.NotEmpty(locks);
        Assert.True(locks[^1].Bool);
    }

    [Fact]
    public void A_player_with_no_key_cannot_lock_the_door()
    {
        using var rig = Rig(saved: OwnedDoor());
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        SeeDoor(rig, joel);
        dennis.Send(LevelRpc.Event(dennis.SmallId, Door, ToggleEvent));

        Assert.DoesNotContain(LevelRpc.Heard(rig.World, joel, Door, LockedVariable, RpcKind.Bool),
            v => v.Bool);

        Assert.Equal(1, rig.LinesSaying("Dennis has no key"));
    }
}

/// <summary>
/// Phones built into the level actually working: calling, the display, hanging up
/// and staying silent when nothing is going on.
///
/// The same rig as EvoCityLevelTests, a real server with the real phones plugin
/// off disk. Everything asserted here is read off the wire.
/// </summary>
[Trait("Needs", "fusion-server-mods")]
public class EvoCityPhoneCallTests
{
    // Wire indices: Fusion registers a level host's components in the reverse of the builder's order.
    private const ushort KindVariable = 6;
    private const ushort MyNumberVariable = 7;
    private const ushort DigitVariable = 8;
    private const ushort DisplayVariable = 5;
    private const ushort ChannelVariable = 4;
    private const ushort InCallVariable = 3;
    private const ushort RingMutedVariable = 2;
    private const ushort ToneMutedVariable = 1;
    private const ushort RingbackMutedVariable = 0;

    private const ushort HangUpEvent = 7;
    private const ushort ClearEvent = 6;
    private const ushort CallEvent = 5;
    private const ushort LiftEvent = 4;

    private const int LevelKind = 0;

    /// <summary>What a payphone shows with nothing dialled.</summary>
    private const string Waiting = "---";

    private const ulong Joel = 76561198000000001;
    private const ulong Dennis = 76561198000000002;

    private static string PhoneAt(int i) => LevelRpc.Hash((uint)(0x2000 + i));

    private static LevelRig Rig()
    {
        Assert.True(PluginSource.Repository != null,
            "The fusion-server-mods checkout was not found. Set FUSION_SERVER_MODS to it.");

        Assert.True(PluginSource.Has("phones"),
            $"Build the plugins first: no phones in '{PluginSource.Directory}'.");

        var rig = new LevelRig(
            new ServerConfig { CullOrphanedEntities = false, RpcMessagesPerSecond = 250 },
            new[] { "phones" });

        Assert.Equal(1, rig.Started);

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

    /// <summary>What a phone last heard on one of its components.</summary>
    private static string LastString(LevelRig rig, FakePlayer player, string phone, ushort index)
    {
        var heard = LevelRpc.Heard(rig.World, player, phone, index, RpcKind.String);
        return heard.Count == 0 ? "" : heard[^1].Text;
    }

    private static bool? LastBool(LevelRig rig, FakePlayer player, string phone, ushort index)
    {
        var heard = LevelRpc.Heard(rig.World, player, phone, index, RpcKind.Bool);
        return heard.Count == 0 ? null : heard[^1].Bool;
    }

    /// <summary>A phone built into the level saying what it is, which is what puts it on the exchange.</summary>
    private static string Adopt(LevelRig rig, FakePlayer player, int which)
    {
        string phone = PhoneAt(which);
        player.Send(LevelRpc.Int(player.SmallId, phone, KindVariable, LevelKind));

        Assert.NotEqual("", LastString(rig, player, phone, MyNumberVariable));

        return phone;
    }

    private static string NumberOf(LevelRig rig, FakePlayer player, string phone)
        => LastString(rig, player, phone, MyNumberVariable);

    /// <summary>Taps a number into a phone's keypad and presses call.</summary>
    private static void DialFrom(FakePlayer player, string phone, string number)
    {
        foreach (char digit in number)
        {
            player.Send(LevelRpc.Int(player.SmallId, phone, DigitVariable, digit - '0'));
        }

        player.Send(LevelRpc.Event(player.SmallId, phone, CallEvent));
    }

    // ---- the digits show as they are dialled ----

    [Fact]
    public void Each_keypress_lands_on_the_display()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string phone = Adopt(rig, joel, 0);

        Assert.Equal(Waiting, LastString(rig, joel, phone, DisplayVariable));

        joel.Send(LevelRpc.Int(joel.SmallId, phone, DigitVariable, 4));
        Assert.Equal("4", LastString(rig, joel, phone, DisplayVariable));

        joel.Send(LevelRpc.Int(joel.SmallId, phone, DigitVariable, 1));
        Assert.Equal("41", LastString(rig, joel, phone, DisplayVariable));

        joel.Send(LevelRpc.Int(joel.SmallId, phone, DigitVariable, 7));
        Assert.Equal("417", LastString(rig, joel, phone, DisplayVariable));
    }

    [Fact]
    public void The_clear_key_puts_the_display_back_to_waiting()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string phone = Adopt(rig, joel, 0);

        joel.Send(LevelRpc.Int(joel.SmallId, phone, DigitVariable, 4));
        joel.Send(LevelRpc.Event(joel.SmallId, phone, ClearEvent));

        Assert.Equal(Waiting, LastString(rig, joel, phone, DisplayVariable));
    }

    [Fact]
    public void The_dialled_digits_are_cleared_once_the_call_is_placed()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        DialFrom(joel, a, NumberOf(rig, joel, b));

        // The display moved on to the call, and nothing of the dialled number is left behind.
        joel.Send(LevelRpc.Event(joel.SmallId, a, ClearEvent));

        Assert.Equal(Waiting, LastString(rig, joel, a, DisplayVariable));
    }

    // ---- a call connects ----

    [Fact]
    public void Dialling_a_phone_rings_it_and_gives_the_caller_ringback()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        string numberA = NumberOf(rig, joel, a);
        string numberB = NumberOf(rig, joel, b);

        DialFrom(joel, a, numberB);

        // The mute flag is the value, so false is the sound playing.
        Assert.False(LastBool(rig, joel, b, RingMutedVariable));
        Assert.False(LastBool(rig, joel, a, RingbackMutedVariable));

        Assert.Equal($"{numberA} calling", LastString(rig, joel, b, DisplayVariable));
        Assert.Equal($"Ringing {numberB}", LastString(rig, joel, a, DisplayVariable));
    }

    [Fact]
    public void Lifting_the_handset_joins_the_two_phones_on_one_channel()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        string numberA = NumberOf(rig, joel, a);
        string numberB = NumberOf(rig, joel, b);

        DialFrom(joel, a, numberB);
        joel.Send(LevelRpc.Event(joel.SmallId, b, LiftEvent));

        Assert.True(LastBool(rig, joel, a, InCallVariable));
        Assert.True(LastBool(rig, joel, b, InCallVariable));

        string channelA = LastString(rig, joel, a, ChannelVariable);
        string channelB = LastString(rig, joel, b, ChannelVariable);

        Assert.Equal(channelA, channelB);
        Assert.StartsWith("c", channelA);
        Assert.NotEqual(numberA, channelA);

        Assert.Equal($"On to {numberB}", LastString(rig, joel, a, DisplayVariable));
        Assert.Equal($"On to {numberA}", LastString(rig, joel, b, DisplayVariable));
    }

    [Fact]
    public void Both_bells_stop_once_the_call_is_answered()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        DialFrom(joel, a, NumberOf(rig, joel, b));
        joel.Send(LevelRpc.Event(joel.SmallId, b, LiftEvent));

        Assert.True(LastBool(rig, joel, b, RingMutedVariable));
        Assert.True(LastBool(rig, joel, a, RingbackMutedVariable));
    }

    // ---- hanging up ----

    [Fact]
    public void Hanging_up_ends_the_call_on_both_phones()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        string numberA = NumberOf(rig, joel, a);
        string numberB = NumberOf(rig, joel, b);

        DialFrom(joel, a, numberB);
        joel.Send(LevelRpc.Event(joel.SmallId, b, LiftEvent));
        joel.Send(LevelRpc.Event(joel.SmallId, a, HangUpEvent));

        Assert.False(LastBool(rig, joel, a, InCallVariable));
        Assert.False(LastBool(rig, joel, b, InCallVariable));

        Assert.Equal(numberA, LastString(rig, joel, a, ChannelVariable));
        Assert.Equal(numberB, LastString(rig, joel, b, ChannelVariable));
    }

    [Fact]
    public void Hanging_up_while_it_is_still_ringing_stops_the_ring()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        DialFrom(joel, a, NumberOf(rig, joel, b));

        Assert.False(LastBool(rig, joel, b, RingMutedVariable));

        joel.Send(LevelRpc.Event(joel.SmallId, a, HangUpEvent));

        Assert.True(LastBool(rig, joel, b, RingMutedVariable));
        Assert.True(LastBool(rig, joel, a, RingbackMutedVariable));
    }

    // ---- a number that is not there ----

    [Fact]
    public void Calling_a_number_nobody_answers_on_says_so_and_rings_nothing()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        DialFrom(joel, a, "999");

        Assert.Equal("ERR", LastString(rig, joel, a, DisplayVariable));
        Assert.True(LastBool(rig, joel, a, RingbackMutedVariable));
        Assert.True(LastBool(rig, joel, b, RingMutedVariable));
    }

    [Fact]
    public void A_phone_cannot_call_itself()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);

        DialFrom(joel, a, NumberOf(rig, joel, a));

        Assert.Equal("ERR", LastString(rig, joel, a, DisplayVariable));
        Assert.True(LastBool(rig, joel, a, RingbackMutedVariable));
    }

    // ---- the idle state is silent ----

    [Fact]
    public void A_freshly_adopted_phone_has_every_loop_muted()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string phone = Adopt(rig, joel, 0);

        Assert.True(LastBool(rig, joel, phone, RingMutedVariable));
        Assert.True(LastBool(rig, joel, phone, ToneMutedVariable));
        Assert.True(LastBool(rig, joel, phone, RingbackMutedVariable));
    }

    [Fact]
    public void Every_phone_in_the_level_is_silent_once_the_level_has_loaded()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");

        var announces = new List<byte[]>();

        for (var i = 0; i < 59; i++)
        {
            announces.Add(LevelRpc.Int(joel.SmallId, PhoneAt(i), KindVariable, LevelKind));
        }

        joel.SendMany(announces);

        for (var i = 0; i < 59; i++)
        {
            string phone = PhoneAt(i);

            Assert.True(LastBool(rig, joel, phone, RingMutedVariable), $"phone {i} ring");
            Assert.True(LastBool(rig, joel, phone, ToneMutedVariable), $"phone {i} tone");
            Assert.True(LastBool(rig, joel, phone, RingbackMutedVariable), $"phone {i} ringback");
        }
    }

    [Fact]
    public void Both_phones_go_back_to_silent_once_a_call_is_over()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        DialFrom(joel, a, NumberOf(rig, joel, b));
        joel.Send(LevelRpc.Event(joel.SmallId, b, LiftEvent));
        joel.Send(LevelRpc.Event(joel.SmallId, a, HangUpEvent));

        foreach (string phone in new[] { a, b })
        {
            Assert.True(LastBool(rig, joel, phone, RingMutedVariable));
            Assert.True(LastBool(rig, joel, phone, ToneMutedVariable));
            Assert.True(LastBool(rig, joel, phone, RingbackMutedVariable));
        }
    }

    [Fact]
    public void A_clients_own_copy_of_a_mute_flag_never_reaches_anybody_else()
    {
        // The value is the mute flag, so a stray false unmutes a clip that has been
        // looping since the scene loaded. This is what "the phones ring constantly" was.
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        string phone = Adopt(rig, joel, 0);

        dennis.Send(LevelRpc.Bool(dennis.SmallId, phone, RingMutedVariable, false));

        Assert.True(LastBool(rig, joel, phone, RingMutedVariable));
        Assert.True(LastBool(rig, dennis, phone, RingMutedVariable));
    }

    [Fact]
    public void A_mute_flag_sent_before_the_phone_is_adopted_never_reaches_anybody_else()
    {
        // A joining client sends what its own copy holds, and a fresh copy holds
        // "not muted". It can arrive before the phone has said what it is.
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        string phone = PhoneAt(0);

        dennis.Send(LevelRpc.Bool(dennis.SmallId, phone, RingMutedVariable, false));

        Assert.DoesNotContain(LevelRpc.Heard(rig.World, joel, phone, RingMutedVariable, RpcKind.Bool),
            v => !v.Bool);
    }

    // ---- two phones stay in step ----

    [Fact]
    public void Only_the_phone_that_was_dialled_rings()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);
        string c = Adopt(rig, joel, 2);

        DialFrom(joel, a, NumberOf(rig, joel, b));

        Assert.False(LastBool(rig, joel, b, RingMutedVariable));
        Assert.True(LastBool(rig, joel, a, RingMutedVariable));
        Assert.True(LastBool(rig, joel, c, RingMutedVariable));
        Assert.True(LastBool(rig, joel, b, RingbackMutedVariable));
    }

    [Fact]
    public void A_phone_already_on_a_call_cannot_be_rung_again()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);
        string c = Adopt(rig, joel, 2);

        string numberB = NumberOf(rig, joel, b);

        DialFrom(joel, a, numberB);
        joel.Send(LevelRpc.Event(joel.SmallId, b, LiftEvent));

        string channel = LastString(rig, joel, b, ChannelVariable);

        DialFrom(joel, c, numberB);

        Assert.Equal("ERR", LastString(rig, joel, c, DisplayVariable));
        Assert.Equal(channel, LastString(rig, joel, b, ChannelVariable));
        Assert.True(LastBool(rig, joel, c, RingbackMutedVariable));
    }

    [Fact]
    public void A_player_who_joins_during_a_call_sees_it_the_same_way()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        DialFrom(joel, a, NumberOf(rig, joel, b));
        joel.Send(LevelRpc.Event(joel.SmallId, b, LiftEvent));

        var dennis = Arrive(rig, Dennis, "Dennis");

        foreach (string phone in new[] { a, b })
        {
            Assert.Equal(LastBool(rig, joel, phone, InCallVariable),
                LastBool(rig, dennis, phone, InCallVariable));

            Assert.Equal(LastString(rig, joel, phone, ChannelVariable),
                LastString(rig, dennis, phone, ChannelVariable));

            Assert.Equal(LastBool(rig, joel, phone, RingMutedVariable),
                LastBool(rig, dennis, phone, RingMutedVariable));
        }

        Assert.True(LastBool(rig, dennis, a, InCallVariable));
    }

    [Fact]
    public void A_player_who_joins_while_it_is_ringing_hears_the_same_phone_ring()
    {
        using var rig = Rig();
        var joel = Arrive(rig, Joel, "Joel");
        string a = Adopt(rig, joel, 0);
        string b = Adopt(rig, joel, 1);

        DialFrom(joel, a, NumberOf(rig, joel, b));

        var dennis = Arrive(rig, Dennis, "Dennis");

        Assert.False(LastBool(rig, dennis, b, RingMutedVariable));
        Assert.True(LastBool(rig, dennis, a, RingMutedVariable));
        Assert.False(LastBool(rig, dennis, a, RingbackMutedVariable));
    }
}
