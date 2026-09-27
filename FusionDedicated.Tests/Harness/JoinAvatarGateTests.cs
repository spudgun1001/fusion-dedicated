using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Harness;

/// <summary>A player who joins already wearing a refused avatar is turned away, as a swap to it would be.</summary>
public class JoinAvatarGateTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    private const string Sick = "therot.CustomLauncher.Avatar.Sickypigg";

    private static HSteamNetConnection AskWearing(World world, ulong platformId, string name, string barcode)
    {
        var connection = world.Transport.Connect();
        world.Transport.Deliver(connection,
            ClientMessages.Join(world.Server.Config, platformId, name, avatarBarcode: barcode));
        world.Server.Receive();
        world.Sync();

        return connection;
    }

    private static bool ToldAbout(World world, HSteamNetConnection connection, ulong platformId)
        => world.Transport.SentTo(connection)
            .Select(sent => Envelope.Read(sent.Message))
            .OfType<Envelope>()
            .Any(envelope => envelope.Tag == FusionProtocol.TagConnectionResponse
                             && new FusionNetReader(envelope.Payload).ReadUInt64() == platformId);

    [Fact]
    public void A_join_wearing_a_blacklisted_avatar_is_refused_and_nobody_hears_of_it()
    {
        var config = new ServerConfig { CullOrphanedEntities = false };
        config.BlacklistedBarcodes.Add(Sick);
        using var world = new World(config);
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        var connection = AskWearing(world, KanzaId, "Kanza", Sick);

        Assert.Null(world.Server.Players.GetByPlatformId(KanzaId));
        Assert.Equal($"Change your avatar: {Sick} is not allowed here", ConnectionCloseTests.RefusalSentTo(world, connection));
        Assert.False(ToldAbout(world, joel.Connection, KanzaId));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message.Contains($"Kanza joined wearing '{Sick}'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_join_wearing_an_avatar_a_plugin_refuses_is_refused()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Avatar.Subscribe("avatars", e => e.Barcode == Sick ? PluginVerdict.Refuse("banned") : PluginVerdict.Allow);
        world.Server.Plugins = events;

        var connection = AskWearing(world, KanzaId, "Kanza", Sick);

        Assert.Null(world.Server.Players.GetByPlatformId(KanzaId));
        Assert.Equal($"Change your avatar: {Sick} is not allowed here", ConnectionCloseTests.RefusalSentTo(world, connection));
    }

    [Fact]
    public void A_join_wearing_an_allowed_avatar_gets_in()
    {
        var config = new ServerConfig { CullOrphanedEntities = false };
        config.BlacklistedBarcodes.Add(Sick);
        using var world = new World(config);

        AskWearing(world, KanzaId, "Kanza", "SLZ.BONELAB.Content.Avatar.FordBW");

        Assert.NotNull(world.Server.Players.GetByPlatformId(KanzaId));
    }
}
