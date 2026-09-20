using System.Diagnostics;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Harness;

/// <summary>What one measured piece of work cost the server.</summary>
public readonly record struct LoadSample(
    string Label, int Players, int Entities, double Milliseconds, long Bytes, long Sends, long SendBytes)
{
    public double PerPlayer => Players > 0 ? Milliseconds / Players : Milliseconds;

    public override string ToString()
        => $"{Label,-34} players={Players,3} entities={Entities,5} " +
           $"{Milliseconds,9:F2} ms  {Bytes / 1024.0,10:F0} KiB alloc  " +
           $"{Sends,7} sends  {SendBytes / 1024.0,9:F0} KiB out";
}

/// <summary>
/// A real server under a full lobby, with no client models in the way.
///
/// The EvoCity rig replays everything into a ClientView so a test can assert what a
/// player sees. That costs more than the server does, so a load run drives the same
/// real FusionServer over the same FakeTransport and measures only the server.
/// </summary>
public sealed class LoadRig : IDisposable
{
    private readonly string _logs = Path.Combine(
        Path.GetTempPath(), "fusion-load-" + Guid.NewGuid().ToString("N"));

    private readonly List<LoadPlayer> _players = new();
    private ushort _nextEntity = EntityRegistry.FirstEntityId;

    public LoadRig(ServerConfig? config = null)
    {
        Config = config ?? Lobby();
        Config.LogDirectory = _logs;

        Transport = new FakeTransport { KeepSent = false, CheckCanonical = false };

        Server = new FusionServer(Config, Transport) { Clock = () => Now };
        Server.Start();
    }

    /// <summary>The owner's shape: fifty players, two thousand entities, culling left alone.</summary>
    public static ServerConfig Lobby(int rpcBudget = 250) => new()
    {
        MaxPlayers = 50,
        MaxEntities = 2000,
        MaxEntitiesPerPlayer = 300,
        RpcMessagesPerSecond = rpcBudget,
        CullOrphanedEntities = false,
    };

    public ServerConfig Config { get; }

    public FakeTransport Transport { get; }

    public FusionServer Server { get; }

    public DateTime Now { get; private set; } = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    public IReadOnlyList<LoadPlayer> Players => _players;

    public void Advance(TimeSpan by)
    {
        Now += by;
        Server.PumpDeferred();
    }

    /// <summary>Joins a player and lets their game say it has finished loading.</summary>
    public LoadPlayer Join(int index, bool finishLoading = true)
    {
        ulong platformId = 76561198000000000UL + (ulong)index;

        var connection = Transport.Connect();
        Transport.Deliver(connection, ClientMessages.Join(Config, platformId, "Player" + index));
        Server.Receive();

        var joined = Server.Players.GetByPlatformId(platformId)
                     ?? throw new InvalidOperationException($"Player{index} was not let in. " +
                        string.Join(" | ", Server.RecentLog(6).Select(e => e.Message)));

        var player = new LoadPlayer(this, connection, joined.SmallId, platformId);
        _players.Add(player);

        if (finishLoading)
        {
            player.FinishLoading();
        }

        return player;
    }

    public void Fill(int count)
    {
        for (int i = _players.Count; i < count; i++)
        {
            Join(i + 1);
        }
    }

    /// <summary>Puts props in the world, shared out between the players who are here.</summary>
    public List<ushort> Populate(int count, string barcode = "fake.prop.Pistol")
    {
        var ids = new List<ushort>(count);

        for (var i = 0; i < count; i++)
        {
            byte owner = _players.Count > 0 ? _players[i % _players.Count].SmallId : (byte)1;
            ushort id = _nextEntity++;

            var entity = Server.Entities.Register(id, barcode, owner, i % 100, 0f, i / 100f);
            entity.Source = FusionProtocol.SourcePlayer;

            ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// Fills the RPC variable cache the way a level's own components do: no entity, a
    /// hash, and a component index, which is what EvoCity puts on the wire as it loads.
    /// </summary>
    /// <param name="spread">
    /// True to move the clock a second per allowance, so every variable gets past the
    /// RPC budget. False sends them in one frame, which is how a level really loads.
    /// </param>
    public int LoadLevelVariables(LoadPlayer author, int count, bool spread = true)
    {
        int budget = Math.Max(1, Config.RpcMessagesPerSecond);

        for (var i = 0; i < count; i += budget)
        {
            var batch = new List<byte[]>(budget);

            for (int j = i; j < Math.Min(count, i + budget); j++)
            {
                batch.Add(LevelRpc.Bool(author.SmallId, LevelRpc.Hash((uint)(0x200000 + j)),
                    (ushort)(j % 64), j % 2 == 0));
            }

            author.SendMany(batch);

            if (!spread)
            {
                continue;
            }

            Now += TimeSpan.FromSeconds(1);
        }

        return count;
    }

    /// <summary>Times one piece of work and says what it allocated and sent.</summary>
    public LoadSample Measure(string label, Action work)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Transport.ResetCounters();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();

        work();

        watch.Stop();
        long after = GC.GetAllocatedBytesForCurrentThread();

        return new LoadSample(label, _players.Count, Server.Entities.Count,
            watch.Elapsed.TotalMilliseconds, after - before, Transport.Sends, Transport.SendBytes);
    }

    public void Dispose()
    {
        Server.Dispose();

        try { Directory.Delete(_logs, true); } catch { }
    }
}

/// <summary>A player on the wire and nothing more: no view, no bookkeeping.</summary>
public sealed class LoadPlayer
{
    private readonly LoadRig _rig;

    internal LoadPlayer(LoadRig rig, HSteamNetConnection connection, byte smallId, ulong platformId)
    {
        _rig = rig;
        Connection = connection;
        SmallId = smallId;
        PlatformId = platformId;
    }

    public HSteamNetConnection Connection { get; }

    public byte SmallId { get; }

    public ulong PlatformId { get; }

    public void Send(byte[] message)
    {
        _rig.Transport.Deliver(Connection, message);
        _rig.Server.Receive();
    }

    /// <summary>Delivers them all, then runs the loop until nothing is left queued.</summary>
    public void SendMany(IEnumerable<byte[]> messages)
    {
        foreach (byte[] message in messages)
        {
            _rig.Transport.Deliver(Connection, message);
        }

        // One Receive handles at most 2048, and a level writes far more than that in a frame.
        do
        {
            _rig.Server.Receive();
        }
        while (_rig.Transport.Pending > 0);
    }

    public void FinishLoading()
    {
        Send(ClientMessages.FinishedLoading(SmallId));
        Send(FusionProtocol.BuildPlayerPoseUpdate(SmallId, new FusionRigPose { PelvisPosition = Pelvis }));
    }

    /// <summary>Where this player stands, which pose thinning and the fly check both read.</summary>
    public Vec3 Pelvis { get; set; } = Vec3.Zero;

    /// <summary>One pose per prop, the way an owner's game reports what it simulates.</summary>
    public IEnumerable<byte[]> Poses(IEnumerable<ushort> entities)
        => entities.Select(id => FusionProtocol.BuildEntityPoseUpdate(
            SmallId, id, new Vec3(1f, 2f, 3f), Quat.Identity, Vec3.Zero, Vec3.Zero));

    /// <summary>What a client asks the moment it builds a prop it was told about.</summary>
    public IEnumerable<byte[]> DataRequests(IEnumerable<ushort> entities, byte owner)
        => entities.Select(id => FusionProtocol.BuildEntityDataRequest(SmallId, owner, id));
}
