using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Harness;

/// <summary>A real server with fake players on a fake transport and a clock the test moves.</summary>
public sealed class World : IDisposable
{
    private readonly List<FakePlayer> _players = new();

    public World(ServerConfig? config = null)
    {
        Server = new FusionServer(config ?? new ServerConfig { CullOrphanedEntities = false }, Transport)
        {
            Clock = () => Now,
        };

        Server.Start();
    }

    public FakeTransport Transport { get; } = new();

    public FusionServer Server { get; }

    public DateTime Now { get; private set; } = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    public IReadOnlyList<FakePlayer> Players => _players;

    public FakePlayer Join(ulong platformId, string name)
    {
        var connection = Transport.Connect();
        Transport.Deliver(connection, ClientMessages.Join(Server.Config, platformId, name));
        Server.Receive();

        var joined = Server.Players.GetByPlatformId(platformId)
                     ?? throw new InvalidOperationException($"{name} was not let in");

        var player = new FakePlayer(this, connection, joined.SmallId, name);
        _players.Add(player);
        Sync();

        return player;
    }

    public void Leave(FakePlayer player, string reason)
    {
        Transport.Disconnect(player.Connection, reason);
        _players.Remove(player);
        Sync();
    }

    /// <summary>Puts a prop in the world the way a spawn does: the server keeps it and tells everyone here.</summary>
    public void Spawn(FakePlayer owner, ushort id, string barcode, float x, float y, float z)
    {
        var entity = Server.Entities.Register(id, barcode, owner.SmallId, x, y, z);
        entity.Source = FusionProtocol.SourcePlayer;

        Server.Broadcast(FusionProtocol.BuildSpawnResponse(owner.SmallId, owner.SmallId, id, barcode,
            new Vec3(x, y, z), rotation: null, trackerId: 0), reliable: true);
        Sync();
    }

    public void Advance(TimeSpan by)
    {
        Now += by;
        Server.PumpDeferred();
        Sync();
    }

    /// <summary>Feeds every player's view whatever the server has sent them since last time.</summary>
    public void Sync()
    {
        foreach (var player in _players)
        {
            player.CatchUp();
        }
    }

    public Dictionary<string, byte?> OwnersOf(ushort entity)
        => _players.ToDictionary(p => p.Name,
            p => p.View.Entities.TryGetValue(entity, out var seen) ? seen.Owner : null);

    public bool AgreeOnOwner(ushort entity) => OwnersOf(entity).Values.Distinct().Count() == 1;

    public bool SlotsAgree()
        => _players.Select(p => string.Join(";", p.View.Slots.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value}")))
            .Distinct().Count() <= 1;

    public void Dispose() => Server.Dispose();
}

public sealed class FakePlayer
{
    private readonly World _world;
    private int _read;

    internal FakePlayer(World world, HSteamNetConnection connection, byte smallId, string name)
    {
        _world = world;
        Connection = connection;
        SmallId = smallId;
        Name = name;
        View = new ClientView(smallId);
    }

    public HSteamNetConnection Connection { get; }

    public byte SmallId { get; }

    public string Name { get; }

    public ClientView View { get; }

    public int MessagesReceived { get; private set; }

    public long BytesReceived { get; private set; }

    public void Send(byte[] message)
    {
        SeeOwn(message);
        _world.Transport.Deliver(Connection, message);
        _world.Server.Receive();
        _world.Sync();
    }

    public void SendMany(IEnumerable<byte[]> messages)
    {
        foreach (byte[] message in messages)
        {
            SeeOwn(message);
            _world.Transport.Deliver(Connection, message);
        }

        _world.Server.Receive();
        _world.Sync();
    }

    /// <summary>A real client already shows what it did itself, and the server never sends relay 3 back to its sender. Poses are left out because a client never judges its own.</summary>
    private void SeeOwn(byte[] message)
    {
        if (message.Length > 1 && message[1] == 3 && message[0] != FusionProtocol.TagEntityPoseUpdate)
        {
            View.Receive(message);
        }
    }

    public void FinishLoading()
    {
        View.MarkLoaded();
        Send(ClientMessages.FinishedLoading(SmallId));
    }

    internal void CatchUp()
    {
        var sent = _world.Transport.SentTo(Connection);

        for (; _read < sent.Count; _read++)
        {
            MessagesReceived++;
            BytesReceived += sent[_read].Message.Length;
            View.Receive(sent[_read].Message);
        }
    }
}
