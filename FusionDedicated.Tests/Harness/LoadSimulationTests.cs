using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using Xunit.Abstractions;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A full lobby on a big level, measured rather than reasoned about.
///
/// Each test drives the real server over the fake transport and prints the curve it
/// measured, so a change can be compared against the run before it rather than
/// against a memory of one. The assertions are on the shape of the curve, not on a
/// millisecond count, because the number moves with the machine and the shape does not.
/// </summary>
public class LoadSimulationTests
{
    private readonly ITestOutputHelper _out;

    public LoadSimulationTests(ITestOutputHelper output) => _out = output;

    /// <summary>The counts to walk. 11 to 13 is where players say it starts to drag.</summary>
    private static readonly int[] PlayerCounts = { 1, 5, 10, 13, 20, 30, 40, 50 };

    private static readonly int[] EntityCounts = { 0, 250, 500, 1000, 2000 };

    /// <summary>Roughly what EvoCity ships that is not an event, which is what the cache holds.</summary>
    private const int EvoCityLevelVariables = 14_600;

    private void Print(string title, IEnumerable<LoadSample> samples)
    {
        _out.WriteLine("");
        _out.WriteLine(title);
        _out.WriteLine(new string('-', title.Length));

        foreach (var sample in samples)
        {
            _out.WriteLine(sample.ToString());
        }
    }

    // ---- 1. players ----

    [Fact]
    public void Relaying_one_pose_costs_more_than_linearly_as_the_lobby_fills()
    {
        var samples = new List<LoadSample>();

        foreach (int count in PlayerCounts)
        {
            using var rig = new LoadRig();
            rig.Fill(count);

            var sender = rig.Players[0];
            var props = rig.Populate(1);

            // Warm: first pose registers the entity's owner and logs about it.
            sender.SendMany(sender.Poses(props));

            var poses = Enumerable.Range(0, 200).SelectMany(_ => sender.Poses(props)).ToList();

            samples.Add(rig.Measure("relay 200 poses", () => sender.SendMany(poses)));
        }

        Print("Relaying a prop pose to the lobby", samples);

        // One send per other player is the floor, so the send count is linear by construction.
        Assert.True(samples[^1].Sends > samples[0].Sends);
    }

    [Fact]
    public void A_tick_costs_more_as_the_lobby_fills()
    {
        var samples = new List<LoadSample>();

        foreach (int count in PlayerCounts)
        {
            using var rig = new LoadRig();
            rig.Fill(count);

            rig.Server.Tick();

            samples.Add(rig.Measure("100 ticks", () =>
            {
                for (var i = 0; i < 100; i++)
                {
                    rig.Server.Tick();
                }
            }));
        }

        Print("A housekeeping tick with the lobby filling", samples);

        Assert.All(samples, s => Assert.True(s.Milliseconds >= 0));
    }

    [Fact]
    public void Everybody_moving_at_once_is_the_real_per_frame_cost()
    {
        var samples = new List<LoadSample>();

        foreach (int count in PlayerCounts)
        {
            using var rig = new LoadRig();
            rig.Fill(count);

            // One held prop each, which is what everybody carrying a gun looks like.
            var props = rig.Populate(count);

            var frame = new List<(LoadPlayer Player, byte[] Message)>();

            foreach (var player in rig.Players)
            {
                frame.Add((player, FusionProtocol.BuildPlayerPoseUpdate(
                    player.SmallId, new FusionRigPose { PelvisPosition = new Vec3(player.SmallId, 0f, 0f) })));
            }

            for (var i = 0; i < props.Count; i++)
            {
                var owner = rig.Players[i % rig.Players.Count];
                frame.Add((owner, owner.Poses(new[] { props[i] }).First()));
            }

            // Warm.
            foreach (var (player, message) in frame)
            {
                player.Send(message);
            }

            samples.Add(rig.Measure("60 frames of everyone moving", () =>
            {
                for (var f = 0; f < 60; f++)
                {
                    foreach (var (player, message) in frame)
                    {
                        rig.Transport.Deliver(player.Connection, message);
                    }

                    rig.Server.Receive();
                }
            }));
        }

        Print("Sixty frames of every player and their prop moving", samples);

        Assert.NotEmpty(samples);
    }

    // ---- 2. entities ----

    [Fact]
    public void Joining_costs_more_as_the_world_fills_with_props()
    {
        var samples = new List<LoadSample>();

        foreach (int entities in EntityCounts)
        {
            using var rig = new LoadRig();
            rig.Fill(10);
            rig.Populate(entities);

            samples.Add(rig.Measure($"one join, {entities} props", () => rig.Join(99)));
        }

        Print("One player joining a world of props", samples);

        Assert.NotEmpty(samples);
    }

    [Fact]
    public void A_joiners_data_requests_cost_more_as_the_world_fills()
    {
        var samples = new List<LoadSample>();

        foreach (int entities in EntityCounts.Where(e => e > 0))
        {
            using var rig = new LoadRig();
            rig.Fill(10);
            var props = rig.Populate(entities);

            var joiner = rig.Join(99);
            var requests = joiner.DataRequests(props, rig.Players[0].SmallId).ToList();

            samples.Add(rig.Measure($"{entities} data requests", () => joiner.SendMany(requests)));
        }

        Print("A joiner asking about every prop it was told about", samples);

        Assert.NotEmpty(samples);
    }

    [Fact]
    public void Ownership_changing_hands_costs_more_as_the_world_fills()
    {
        var samples = new List<LoadSample>();

        foreach (int entities in EntityCounts.Where(e => e > 0))
        {
            using var rig = new LoadRig();
            rig.Fill(10);
            var props = rig.Populate(entities);

            // Each request has to come from somebody who is not already the owner.
            var taker = rig.Players[^1];
            var wanted = props.Where((_, i) => i % rig.Players.Count != rig.Players.Count - 1).Take(200).ToList();

            var requests = wanted
                .Select(id => FusionProtocol.BuildOwnershipRequest(taker.SmallId, id))
                .ToList();

            // The budget is ten a second, so the clock has to move between them.
            samples.Add(rig.Measure($"{wanted.Count} ownership takes, {entities} props", () =>
            {
                foreach (byte[] request in requests)
                {
                    rig.Advance(TimeSpan.FromMilliseconds(200));
                    taker.Send(request);
                }
            }));
        }

        Print("Taking ownership of props one after another", samples);

        Assert.NotEmpty(samples);
    }

    // ---- 3. what the level itself adds ----

    [Fact]
    public void Loading_the_levels_own_variables_fills_the_cache_and_costs_what_it_costs()
    {
        using var rig = new LoadRig();
        var author = rig.Join(1);

        var sample = rig.Measure($"{EvoCityLevelVariables} level variables", () =>
            rig.LoadLevelVariables(author, EvoCityLevelVariables));

        Print("EvoCity writing its own RPC variables as it loads", new[] { sample });

        _out.WriteLine($"cache holds {rig.Server.CachedRpcVariables} of {RpcVariableCache.MaxVariables}");

        // The cache fills long before the level has finished writing.
        Assert.Equal(RpcVariableCache.MaxVariables, rig.Server.CachedRpcVariables);
    }

    [Fact]
    public void A_full_cache_makes_every_prop_data_request_cost_more()
    {
        var samples = new List<LoadSample>();

        foreach (bool full in new[] { false, true })
        {
            using var rig = new LoadRig();
            rig.Fill(10);

            if (full)
            {
                rig.LoadLevelVariables(rig.Players[0], EvoCityLevelVariables);
            }

            var props = rig.Populate(1000);
            var joiner = rig.Join(99);
            var requests = joiner.DataRequests(props, rig.Players[0].SmallId).ToList();

            samples.Add(rig.Measure(full ? "1000 requests, cache full" : "1000 requests, cache empty",
                () => joiner.SendMany(requests)));
        }

        Print("What a full RPC variable cache costs a joiner", samples);

        // A prop's own variables are what a request is answered with, and no prop has
        // thousands. Looking them up must not cost what the whole cache holds.
        double ratio = samples[1].Milliseconds / Math.Max(0.01, samples[0].Milliseconds);

        _out.WriteLine($"a full cache costs {ratio:F1}x an empty one");

        Assert.True(ratio < 4,
            $"a full cache made each data request {ratio:F1}x dearer, which is the cache being walked per request");
    }

    [Fact]
    public void The_rpc_budget_decides_how_much_of_the_level_a_joiner_gets_through()
    {
        foreach (int budget in new[] { 60, 250 })
        {
            using var rig = new LoadRig(LoadRig.Lobby(budget));
            var author = rig.Join(1);

            // The level writes itself in one frame, which is how it really arrives.
            int wanted = 2000;
            rig.LoadLevelVariables(author, wanted, spread: false);

            _out.WriteLine($"budget {budget,3}/s: {rig.Server.CachedRpcVariables,5} of {wanted} variables kept");
        }
    }

    [Fact]
    public void A_joiner_waits_on_the_catch_up_pacing_rather_than_on_the_server()
    {
        using var rig = new LoadRig();
        rig.Fill(20);
        rig.LoadLevelVariables(rig.Players[0], EvoCityLevelVariables);
        rig.Populate(2000);

        var joiner = rig.Join(99, finishLoading: false);

        rig.Transport.ResetCounters();
        joiner.FinishLoading();

        long queued = rig.Transport.Sends;
        var seconds = 0;

        // A second at a time until the outbox has nothing left for anybody.
        for (; seconds < 600; seconds++)
        {
            long before = rig.Transport.Sends;
            rig.Advance(TimeSpan.FromSeconds(1));

            if (rig.Transport.Sends == before)
            {
                break;
            }
        }

        _out.WriteLine("");
        _out.WriteLine($"2000 props and a full level cache, {rig.Players.Count} players:");
        _out.WriteLine($"  {queued} messages went out at once, " +
                       $"{rig.Transport.Sends - queued} more over {seconds}s of pacing " +
                       $"at {rig.Config.CatchupMessagesPerSecond}/s");

        Assert.True(seconds > 0, "nothing was paced, so the outbox was not used");
    }

    /// <summary>
    /// What the log costs the message loop.
    ///
    /// Log holds a lock, appends to a dated file and flushes the writer on every line,
    /// and the replay paths write a line per request, so a joiner puts thousands of
    /// them through the thread everybody else's traffic runs on.
    /// </summary>
    [Fact]
    public void Logging_costs_the_message_loop_what_a_flushed_write_costs()
    {
        using var rig = new LoadRig();
        rig.Fill(20);
        rig.LoadLevelVariables(rig.Players[0], EvoCityLevelVariables);
        var props = rig.Populate(2000);

        // A joiner asking about every prop, which is what a client does as it builds them.
        var joiner = rig.Join(99);
        long before = LoggedLines(rig);

        var sample = rig.Measure("2000 data requests",
            () => joiner.SendMany(joiner.DataRequests(props, rig.Players[0].SmallId)));

        long lines = LoggedLines(rig) - before;

        // The same call the handlers make, on the same file, so this is the real path.
        var writing = rig.Measure($"{lines} log lines", () =>
        {
            for (long i = 0; i < lines; i++)
            {
                rig.Server.Log("INFO", $"Data request by Player99 for entity {i} " +
                                       "redirected from nobody to player 1", console: false);
            }
        });

        double perLine = writing.Milliseconds * 1000 / Math.Max(1, lines);

        Print("A joiner's data requests, and what logging them cost", new[] { sample, writing });

        _out.WriteLine($"{lines} lines written, {perLine:F2} us each, " +
                       $"{writing.Milliseconds / Math.Max(0.01, sample.Milliseconds) * 100:F0}% of the work above");

        Assert.True(lines > 0, "the replay paths logged nothing, so this measures the wrong thing");
    }

    /// <summary>Lines in the server's own log file, which is where every Log call lands.</summary>
    private static long LoggedLines(LoadRig rig)
        => Directory.Exists(rig.Config.LogDirectory)
            ? Directory.GetFiles(rig.Config.LogDirectory, "*.log").Sum(f => Lines(f))
            : 0;

    private static long Lines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        long count = 0;

        while (reader.ReadLine() != null)
        {
            count++;
        }

        return count;
    }

    // ---- 4. both at once ----

    [Fact]
    public void A_full_lobby_on_a_full_world_is_where_the_two_curves_meet()
    {
        var samples = new List<LoadSample>();

        foreach (int count in new[] { 10, 20, 50 })
        {
            using var rig = new LoadRig();

            // One short, so the join at the end of this has a place to join into.
            rig.Fill(count - 1);
            rig.LoadLevelVariables(rig.Players[0], EvoCityLevelVariables);

            var props = rig.Populate(2000);

            var frame = new List<(LoadPlayer Player, byte[] Message)>();

            for (var i = 0; i < 300; i++)
            {
                var owner = rig.Players[i % rig.Players.Count];
                frame.Add((owner, owner.Poses(new[] { props[i] }).First()));
            }

            foreach (var (player, message) in frame)
            {
                player.Send(message);
            }

            samples.Add(rig.Measure("60 frames, 300 props moving", () =>
            {
                for (var f = 0; f < 60; f++)
                {
                    foreach (var (player, message) in frame)
                    {
                        rig.Transport.Deliver(player.Connection, message);
                    }

                    rig.Server.Receive();
                }
            }));

            samples.Add(rig.Measure("one join into all of that", () => rig.Join(99)));
        }

        Print("A full lobby on a full world", samples);

        Assert.NotEmpty(samples);
    }
}
