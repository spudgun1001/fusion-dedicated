using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// The Club Zombo DJ deck against a real server and the real club plugin, at the live RPC budget of 60 and the shipped 250.
/// The plugin learns the deck's level path from its announce, so a made up hash stands in for Southside_Club/Deck.
/// </summary>
public class SouthsideClubTests
{
    private const int DeckAnnounce = 1002;
    private const int DeckEntityAnnounce = 1003;
    private const ushort AnnounceVariable = 0;
    private const ushort UrlVariable = 1;
    private const ushort DeckEntity = 700;
    private const ushort OtherProp = 701;

    /// <summary>The hash Fusion gives the deck's MarrowEntity, which its own RPCInt shares while it is still a level object.</summary>
    private const uint DeckEntityHash = 0xC1DB00E1;

    private const ulong Joel = 76561198000000001;
    private const ulong Dennis = 76561198000000002;
    private const ulong Tony = 76561198000000003;

    private const string Song = "https://example.com/club/track.mp4";

    private static readonly string Deck = LevelRpc.Hash(0xC1DB0001);
    private static readonly string DeckItself = LevelRpc.Hash(DeckEntityHash);

    private static LevelRig Rig(int rpcBudget)
    {
        Assert.True(PluginSource.Repository != null,
            "The fusion-server-mods checkout was not found. Set FUSION_SERVER_MODS to it.");

        string[] plugins = { "doors", "club" };

        Assert.True(plugins.All(PluginSource.Has), $"Build the plugins first in '{PluginSource.Directory}'.");

        var rig = new LevelRig(new ServerConfig { CullOrphanedEntities = false, RpcMessagesPerSecond = rpcBudget }, plugins);

        Assert.Equal(plugins.Length, rig.Started);

        return rig;
    }

    /// <summary>Joins, stands, and has the level say where its deck is, as every game does when the level starts.</summary>
    private static FakePlayer Arrive(LevelRig rig, ulong platformId, string name)
    {
        var player = rig.World.Join(platformId, name);
        player.FinishLoading();
        player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId,
            new FusionRigPose { PelvisPosition = new Vec3(4f, 0f, 9f) }));
        player.Send(LevelRpc.Int(player.SmallId, Deck, AnnounceVariable, DeckAnnounce));
        player.Send(LevelRpc.Int(player.SmallId, DeckItself, AnnounceVariable, DeckEntityAnnounce));

        return player;
    }

    /// <summary>A level object somebody networked by grabbing it, registered and announced as the server sees one.</summary>
    private static void NetworkSceneObject(LevelRig rig, FakePlayer by, ushort entity, uint hash)
    {
        var registered = rig.Server.Entities.Register(entity, "", by.SmallId, 0, 0, 0);
        registered.Discovered = true;
        registered.PositionKnown = false;
        by.Send(FusionProtocol.BuildPropCreate(by.SmallId, unchecked((int)hash), 0, entity));
    }

    private static void NetworkDeck(LevelRig rig, FakePlayer by) => NetworkSceneObject(rig, by, DeckEntity, DeckEntityHash);

    /// <summary>What one game sends on a menu tap: its clipboard, to the url.</summary>
    private static void MenuTap(FakePlayer player, string url)
        => player.Send(LevelRpc.Message(player.SmallId, RpcKind.String, Deck, UrlVariable, RpcValue.OfString(url)));

    /// <summary>An int on a networked entity's own path, which is how a held prop's RPCInt speaks.</summary>
    private static byte[] EntityInt(FakePlayer player, ushort entity, int value)
    {
        byte[] payload = RpcProtocol.WriteValue(RpcKind.Int,
            Convert.FromHexString(RpcProtocol.PathFor(entity, AnnounceVariable)), RpcValue.OfInt(value));
        var message = new FusionNetWriter(payload.Length + 32);

        message.Write((byte)RpcKind.Int);
        message.Write((byte)3);
        message.Write((byte)0);
        message.WriteNullable(player.SmallId);
        message.WriteBlock(payload);

        return message.ToArray();
    }

    private static List<string> Heard(LevelRig rig, FakePlayer player)
        => LevelRpc.Heard(rig.World, player, Deck, UrlVariable, RpcKind.String).Select(v => v.Text).ToList();

    private static string Last(LevelRig rig, FakePlayer player)
        => Heard(rig, player) is { Count: > 0 } heard ? heard[^1] : "";

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_holders_url_reaches_every_player(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        NetworkDeck(rig, joel);
        joel.Grab(DeckEntity);
        MenuTap(joel, Song);

        Assert.Equal(Song, Last(rig, joel));
        Assert.Equal(Song, Last(rig, dennis));
        Assert.Equal(1, rig.LinesSaying("Joel played " + Song));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_url_from_somebody_not_holding_the_deck_is_dropped(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        NetworkDeck(rig, joel);
        MenuTap(dennis, Song);

        Assert.Empty(Heard(rig, joel));
        Assert.Empty(Heard(rig, dennis));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void The_copies_other_games_replay_do_not_overwrite_the_holders_url(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");
        var tony = Arrive(rig, Tony, "Tony");

        NetworkDeck(rig, joel);
        joel.Grab(DeckEntity);

        // Fusion replays Joel's menu tap in every game, and each pastes its own clipboard.
        MenuTap(dennis, "https://example.com/dennis.mp4");
        MenuTap(joel, Song);
        MenuTap(tony, "https://example.com/tony.mp4");

        Assert.All(new[] { joel, dennis, tony }, p => Assert.Equal(new[] { Song }, Heard(rig, p)));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_late_joiner_is_given_the_url_playing(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");

        NetworkDeck(rig, joel);
        joel.Grab(DeckEntity);
        MenuTap(joel, Song);

        var tony = Arrive(rig, Tony, "Tony");

        Assert.Equal(Song, Last(rig, tony));
    }

    [Theory]
    [InlineData(60, 600)]
    [InlineData(250, 600)]
    [InlineData(60, 0)]
    [InlineData(250, 0)]
    public void A_url_too_long_to_keep_or_not_http_is_refused(int budget, int length)
    {
        string url = length > 0 ? "https://example.com/" + new string('a', length - 20) : "file:///C:/Users/joel/track.mp4";

        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        NetworkDeck(rig, joel);
        joel.Grab(DeckEntity);
        MenuTap(joel, Song);
        MenuTap(joel, url);

        Assert.Equal(new[] { Song }, Heard(rig, dennis));
        Assert.Equal(Song, Last(rig, Arrive(rig, Tony, "Tony")));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void A_prop_announcing_itself_as_the_deck_does_not_let_its_holder_play(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        NetworkDeck(rig, joel);
        NetworkSceneObject(rig, dennis, OtherProp, 0xBADBAD01);
        dennis.Grab(OtherProp);

        dennis.Send(EntityInt(dennis, OtherProp, DeckEntityAnnounce));
        MenuTap(dennis, "https://example.com/forged.mp4");

        Assert.Empty(Heard(rig, joel));
        Assert.Empty(Heard(rig, dennis));
        Assert.Single(LevelRpc.Heard(rig.World, joel, RpcProtocol.PathFor(OtherProp, AnnounceVariable), RpcKind.Int));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void Announces_from_anywhere_else_pass_through_and_move_nothing(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");
        string elsewhere = LevelRpc.Hash(0x0BADDECC);

        dennis.Send(LevelRpc.Int(dennis.SmallId, elsewhere, AnnounceVariable, DeckAnnounce));
        dennis.Send(LevelRpc.Int(dennis.SmallId, elsewhere, 2, DeckEntityAnnounce));
        dennis.Send(LevelRpc.Message(dennis.SmallId, RpcKind.String, elsewhere, UrlVariable, RpcValue.OfString(Song)));

        NetworkDeck(rig, joel);
        joel.Grab(DeckEntity);
        MenuTap(joel, Song);

        Assert.Equal(new[] { DeckAnnounce }, LevelRpc.Heard(rig.World, joel, elsewhere, AnnounceVariable, RpcKind.Int).Select(v => v.Int));
        Assert.Equal(new[] { DeckEntityAnnounce }, LevelRpc.Heard(rig.World, joel, elsewhere, 2, RpcKind.Int).Select(v => v.Int));
        Assert.Equal(new[] { Song }, Heard(rig, dennis));
        Assert.Empty(LevelRpc.Heard(rig.World, dennis, elsewhere, UrlVariable, RpcKind.String));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(250)]
    public void Nothing_plays_before_the_deck_has_been_networked(int budget)
    {
        using var rig = Rig(budget);
        var joel = Arrive(rig, Joel, "Joel");
        var dennis = Arrive(rig, Dennis, "Dennis");

        MenuTap(joel, Song);

        Assert.Empty(Heard(rig, dennis));
    }
}
