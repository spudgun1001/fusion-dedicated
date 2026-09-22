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
    private const int DeckHeldAnnounce = 1003;
    private const ushort AnnounceVariable = 0;
    private const ushort UrlVariable = 1;
    private const ushort DeckEntity = 700;

    private const ulong Joel = 76561198000000001;
    private const ulong Dennis = 76561198000000002;
    private const ulong Tony = 76561198000000003;

    private const string Song = "https://example.com/club/track.mp4";

    private static readonly string Deck = LevelRpc.Hash(0xC1DB0001);

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

    private static FakePlayer Arrive(LevelRig rig, ulong platformId, string name)
    {
        var player = rig.World.Join(platformId, name);
        player.FinishLoading();
        player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId,
            new FusionRigPose { PelvisPosition = new Vec3(4f, 0f, 9f) }));
        player.Send(LevelRpc.Int(player.SmallId, Deck, AnnounceVariable, DeckAnnounce));

        return player;
    }

    /// <summary>The deck as a level object somebody has networked by grabbing it, registered as the server does one.</summary>
    private static void NetworkDeck(LevelRig rig, FakePlayer by)
    {
        var deck = rig.Server.Entities.Register(DeckEntity, "", by.SmallId, 0, 0, 0);
        deck.Discovered = true;
        deck.PositionKnown = false;
    }

    /// <summary>What one game sends on a menu tap: the deck entity names itself, then the clipboard goes to the url.</summary>
    private static void MenuTap(FakePlayer player, string url)
    {
        byte[] payload = RpcProtocol.WriteValue(RpcKind.Int,
            Convert.FromHexString(RpcProtocol.PathFor(DeckEntity, AnnounceVariable)), RpcValue.OfInt(DeckHeldAnnounce));
        var named = new FusionNetWriter(payload.Length + 32);

        named.Write((byte)RpcKind.Int);
        named.Write((byte)3);
        named.Write((byte)0);
        named.WriteNullable(player.SmallId);
        named.WriteBlock(payload);

        player.Send(named.ToArray());
        player.Send(LevelRpc.Message(player.SmallId, RpcKind.String, Deck, UrlVariable, RpcValue.OfString(url)));
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
}
