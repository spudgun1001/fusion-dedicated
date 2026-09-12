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
        // Culling reads the wall clock, which the world does not move, so it stays off unless a scenario asks for it.
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

    /// <summary>
    /// Feeds every player's view whatever the server has sent them since last time, then
    /// delivers whatever data requests that raised and lets the server answer them, until
    /// nobody has anything left to ask.
    /// </summary>
    public void Sync()
    {
        for (var round = 0; ; round++)
        {
            // A kicked player stays a fake player here until we drop them, so their
            // frozen view would otherwise still count in AgreeOnOwner and SlotsAgree.
            // Pruned every round, not just at entry, so a kick landing inside a round's
            // Server.Receive drops them before the next round touches their connection.
            _players.RemoveAll(p => Server.Players.GetByConnection(p.Connection) == null);

            bool delivered = false;

            foreach (var player in _players.ToList())
            {
                delivered |= player.CatchUp();
            }

            if (!delivered)
            {
                return;
            }

            if (round == 16)
            {
                throw new InvalidOperationException("players kept asking for entity data after 16 rounds");
            }

            Server.Receive();
        }
    }

    public Dictionary<string, byte?> OwnersOf(ushort entity)
        => _players.ToDictionary(p => p.Name,
            p => p.View.Entities.TryGetValue(entity, out var seen) ? seen.Owner : null);

    public bool AgreeOnOwner(ushort entity)
    {
        var owners = OwnersOf(entity).Values.ToList();
        return owners.Count > 0 && owners.All(o => o.HasValue) && owners.Distinct().Count() == 1;
    }

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

    /// <summary>
    /// Grabs an entity the way a real hand does: the grab goes out, then an ownership request
    /// unless this player already owns it or it is locked to a driver.
    /// </summary>
    public void Grab(ushort entity, FusionProtocol.Handedness hand = FusionProtocol.Handedness.RIGHT)
    {
        Send(FusionProtocol.BuildGrab(SmallId, hand, 0, entity));

        if (View.Entities.TryGetValue(entity, out var seen) && seen.LockedTo == null && seen.Owner != SmallId)
        {
            Send(FusionProtocol.BuildOwnershipRequest(SmallId, entity));
        }
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
        DeliverDataRequests();
        Send(ClientMessages.FinishedLoading(SmallId));
    }

    internal bool CatchUp()
    {
        var sent = _world.Transport.SentTo(Connection);

        for (; _read < sent.Count; _read++)
        {
            MessagesReceived++;
            BytesReceived += sent[_read].Message.Length;
            View.Receive(sent[_read].Message);
        }

        return DeliverDataRequests();
    }

    /// <summary>A real client asks a prop's owner for its state as soon as it builds the prop.</summary>
    private bool DeliverDataRequests()
    {
        var requests = View.TakeDataRequests();

        foreach (var (entity, owner) in requests)
        {
            _world.Transport.Deliver(Connection, FusionProtocol.BuildEntityDataRequest(SmallId, owner, entity));
        }

        return requests.Count > 0;
    }
}
