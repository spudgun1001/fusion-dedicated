using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// The RPC components of a Marrow pallet, as a plugin sees them.
///
/// A pallet on mod.io cannot ship code and the server cannot run the game, so
/// these components are the only thing the two can both speak. Reading the layout
/// wrongly means a plugin sees a value nobody sent, and a prop that never answers.
/// </summary>
public class RpcProtocolTests
{
    /// <summary>A ComponentPathData for a spawned prop: entity 300, component 2.</summary>
    private static byte[] SpawnedPath()
        => new byte[] { 1, 1, 44, 0, 2, 0 };

    /// <summary>One that came with the level, so it carries a hash instead.</summary>
    private static byte[] LevelPath()
        => new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 1, 2, 0, 0, 0, 3 };

    [Fact]
    public void The_six_tags_are_recognised_and_nothing_else_is()
    {
        Assert.Equal(RpcKind.Event, RpcProtocol.KindOf(209));
        Assert.Equal(RpcKind.Int, RpcProtocol.KindOf(210));
        Assert.Equal(RpcKind.Float, RpcProtocol.KindOf(211));
        Assert.Equal(RpcKind.Bool, RpcProtocol.KindOf(212));
        Assert.Equal(RpcKind.String, RpcProtocol.KindOf(213));
        Assert.Equal(RpcKind.Vector3, RpcProtocol.KindOf(214));

        Assert.Equal(RpcKind.None, RpcProtocol.KindOf(200));
        Assert.Equal(RpcKind.None, RpcProtocol.KindOf(215));
    }

    [Fact]
    public void A_spawned_prop_is_named_by_its_entity()
    {
        var path = RpcProtocol.TryReadPath(SpawnedPath());

        Assert.NotNull(path);
        Assert.True(path!.Value.HasEntity);
        Assert.Equal((ushort)300, path.Value.EntityId);
        Assert.Equal((ushort)2, path.Value.ComponentIndex);
        Assert.Equal(RpcProtocol.ShortPathBytes, path.Value.Path.Length);
    }

    [Fact]
    public void One_that_came_with_the_level_is_named_by_its_hash()
    {
        var path = RpcProtocol.TryReadPath(LevelPath());

        Assert.NotNull(path);
        Assert.False(path!.Value.HasEntity);
        Assert.Equal(RpcProtocol.LongPathBytes, path.Value.Path.Length);
    }

    [Fact]
    public void Two_of_the_same_prefab_are_told_apart()
    {
        // The thing most likely to sink a plugin built on this: two payphones
        // holding one number between them.
        var first = RpcProtocol.TryReadPath(new byte[] { 1, 1, 44, 0, 0, 0 })!.Value;
        var second = RpcProtocol.TryReadPath(new byte[] { 1, 1, 45, 0, 0, 0 })!.Value;

        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public void A_path_too_short_to_read_is_refused()
    {
        Assert.Null(RpcProtocol.TryReadPath(new byte[] { 1, 1 }));
        Assert.Null(RpcProtocol.TryReadPath(new byte[] { 0, 0, 0, 0, 0, 1, 9 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(417)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void An_int_round_trips(int value)
    {
        var written = RpcProtocol.WriteValue(RpcKind.Int, SpawnedPath(), RpcValue.OfInt(value));

        Assert.Equal(value, RpcProtocol.ReadValue(RpcKind.Int, written).Int);
    }

    [Fact]
    public void A_string_round_trips()
    {
        var written = RpcProtocol.WriteValue(
            RpcKind.String, LevelPath(), RpcValue.OfString("call-7f31a2"));

        Assert.Equal("call-7f31a2", RpcProtocol.ReadValue(RpcKind.String, written).Text);
    }

    [Fact]
    public void An_empty_string_round_trips_rather_than_reading_as_rubbish()
    {
        var written = RpcProtocol.WriteValue(RpcKind.String, SpawnedPath(), RpcValue.OfString(""));

        Assert.Equal("", RpcProtocol.ReadValue(RpcKind.String, written).Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_bool_round_trips(bool value)
    {
        var written = RpcProtocol.WriteValue(RpcKind.Bool, SpawnedPath(), RpcValue.OfBool(value));

        Assert.Equal(value, RpcProtocol.ReadValue(RpcKind.Bool, written).Bool);
    }

    [Fact]
    public void A_float_round_trips()
    {
        var written = RpcProtocol.WriteValue(RpcKind.Float, SpawnedPath(), RpcValue.OfFloat(1.5f));

        Assert.Equal(1.5f, RpcProtocol.ReadValue(RpcKind.Float, written).Float);
    }

    [Fact]
    public void A_vector_round_trips()
    {
        var written = RpcProtocol.WriteValue(
            RpcKind.Vector3, SpawnedPath(), RpcValue.OfVector(1f, -2f, 3.5f));

        var read = RpcProtocol.ReadValue(RpcKind.Vector3, written);

        Assert.Equal(1f, read.X);
        Assert.Equal(-2f, read.Y);
        Assert.Equal(3.5f, read.Z);
    }

    [Fact]
    public void An_event_carries_the_path_and_nothing_after_it()
    {
        var written = RpcProtocol.WriteValue(RpcKind.Event, SpawnedPath(), RpcValue.Nothing);

        Assert.Equal(RpcProtocol.ShortPathBytes, written.Length);
        Assert.Equal(RpcKind.None, RpcProtocol.ReadValue(RpcKind.Event, written).Kind);
    }

    [Fact]
    public void A_value_cut_short_reads_as_nothing_rather_than_throwing()
    {
        var cut = SpawnedPath().Concat(new byte[] { 1, 2 }).ToArray();

        Assert.Equal(RpcKind.None, RpcProtocol.ReadValue(RpcKind.Int, cut).Kind);
    }
}

/// <summary>How a plugin is offered one, and what it may do about it.</summary>
public class PluginRpcTests
{
    private static PluginRpc Rpc(List<string> log)
        => new(new PluginHealth(), (level, message) => log.Add(level + " " + message));

    private static RpcRequest Request()
        => new(76561198000000001, "Joel", PermissionLevel.Default, RpcKind.Int,
            "01012C000200", true, 300, 2, RpcValue.OfInt(417));

    [Fact]
    public void Nothing_is_watched_until_a_plugin_asks()
    {
        var rpc = Rpc(new List<string>());

        Assert.False(rpc.Watched);

        rpc.Watch("phones", _ => RpcAction.Pass);

        Assert.True(rpc.Watched);
    }

    [Fact]
    public void A_watcher_is_shown_what_arrived()
    {
        var rpc = Rpc(new List<string>());
        RpcRequest? seen = null;

        rpc.Watch("phones", r => { seen = r; return RpcAction.Pass; });
        rpc.Dispatch(Request());

        Assert.Equal(417, seen!.Value.Value.Int);
        Assert.Equal((ushort)300, seen.Value.EntityId);
    }

    [Fact]
    public void Passing_leaves_it_to_carry_on_to_the_other_clients()
        => Assert.Equal(RpcActionKind.Pass, Dispatch(_ => RpcAction.Pass));

    [Fact]
    public void Dropping_keeps_it()
        => Assert.Equal(RpcActionKind.Drop, Dispatch(_ => RpcAction.Drop));

    [Fact]
    public void One_plugin_dropping_does_not_blind_another()
    {
        // Watching is the common case, so everybody is still shown it.
        var rpc = Rpc(new List<string>());
        var seen = 0;

        rpc.Watch("phones", _ => RpcAction.Drop);
        rpc.Watch("gangs", _ => { seen++; return RpcAction.Pass; });

        Assert.Equal(RpcActionKind.Drop, rpc.Dispatch(Request()).Kind);
        Assert.Equal(1, seen);
    }

    [Fact]
    public void A_plugin_that_throws_stops_neither_the_rest_nor_the_message()
    {
        var log = new List<string>();
        var rpc = Rpc(log);
        var seen = 0;

        rpc.Watch("rude", _ => throw new InvalidOperationException("no"));
        rpc.Watch("polite", _ => { seen++; return RpcAction.Pass; });

        Assert.Equal(RpcActionKind.Pass, rpc.Dispatch(Request()).Kind);
        Assert.Equal(1, seen);
        Assert.Contains(log, l => l.Contains("threw on an RPC"));
    }

    [Fact]
    public void Unloading_a_plugin_stops_it_being_shown_any_more()
    {
        var rpc = Rpc(new List<string>());
        var seen = 0;

        rpc.Watch("phones", _ => { seen++; return RpcAction.Pass; });
        rpc.RemoveAll("phones");
        rpc.Dispatch(Request());

        Assert.Equal(0, seen);
        Assert.False(rpc.Watched);
    }

    [Fact]
    public void What_a_plugin_sends_carries_its_path_and_value()
    {
        var rpc = Rpc(new List<string>());
        (RpcKind Kind, string Path, RpcValue Value, ulong? Who)? sent = null;

        rpc.Sender = (kind, path, value, who) => sent = (kind, path, value, who);

        rpc.SetString("AABB", "call-7f31a2", 76561198000000001);

        Assert.Equal(RpcKind.String, sent!.Value.Kind);
        Assert.Equal("AABB", sent.Value.Path);
        Assert.Equal("call-7f31a2", sent.Value.Value.Text);
        Assert.Equal(76561198000000001UL, sent.Value.Who);
    }

    [Fact]
    public void Sending_to_nobody_in_particular_means_everybody()
    {
        var rpc = Rpc(new List<string>());
        ulong? who = 1;

        rpc.Sender = (_, _, _, w) => who = w;
        rpc.SetInt("AABB", 417);

        Assert.Null(who);
    }

    [Fact]
    public void A_path_that_is_not_one_is_not_sent()
    {
        var rpc = Rpc(new List<string>());
        var sent = 0;

        rpc.Sender = (_, _, _, _) => sent++;

        rpc.SetInt("", 1);
        rpc.SetInt("   ", 1);

        Assert.Equal(0, sent);
    }

    [Fact]
    public void A_plugin_outside_a_server_sends_nothing_rather_than_throwing()
    {
        // Sender is null when a plugin is loaded by the tests.
        var rpc = Rpc(new List<string>());

        rpc.SetInt("AABB", 417);
        rpc.FireEvent("AABB");
    }

    private static RpcActionKind Dispatch(Func<RpcRequest, RpcAction> handler)
    {
        var rpc = Rpc(new List<string>());
        rpc.Watch("phones", handler);

        return rpc.Dispatch(Request()).Kind;
    }
}

/// <summary>
/// Building a path rather than waiting to see one.
///
/// A prop has several components and only some of them ever speak. Without this a
/// plugin could answer a phone only on the component that spoke to it, which is
/// never the one that shows the number.
/// </summary>
public class RpcPathBuildingTests
{
    [Fact]
    public void A_built_path_reads_back_as_the_component_it_names()
    {
        var path = RpcProtocol.TryReadPath(Convert.FromHexString(RpcProtocol.PathFor(300, 2)));

        Assert.True(path!.Value.HasEntity);
        Assert.Equal((ushort)300, path.Value.EntityId);
        Assert.Equal((ushort)2, path.Value.ComponentIndex);
    }

    [Fact]
    public void It_matches_one_that_arrived_on_the_wire()
    {
        // The whole point: a path built here has to be the same key as the one a
        // client sent, or the plugin talks to nobody.
        var observed = RpcProtocol.TryReadPath(new byte[] { 1, 1, 44, 0, 2, 0 })!.Value;

        Assert.Equal(observed.Key, RpcProtocol.PathFor(300, 2));
    }

    [Fact]
    public void Every_component_on_one_prop_gets_its_own()
    {
        var paths = Enumerable.Range(0, 6).Select(i => RpcProtocol.PathFor(300, (ushort)i));

        Assert.Equal(6, paths.Distinct().Count());
    }

    [Fact]
    public void Two_props_never_share_one()
    {
        Assert.NotEqual(RpcProtocol.PathFor(300, 0), RpcProtocol.PathFor(301, 0));
    }
}

/// <summary>
/// Turning a place into a key, which is how a plugin remembers something about a
/// prop that has been kept: it comes back after a restart at the same spot with a
/// new entity id and nothing else in common.
/// </summary>
public class PluginWorldTests
{
    private static PluginEntity At(float x, float y, float z, bool kept = true)
        => new(300, "Pack.Spawnable.Payphone", 76561198000000001, x, y, z, kept);

    [Fact]
    public void The_same_spot_gives_the_same_key()
        => Assert.Equal(PluginWorld.PlaceOf(At(4f, 0f, -12f)), PluginWorld.PlaceOf(At(4f, 0f, -12f)));

    [Fact]
    public void A_float_that_drifts_a_little_still_gives_the_same_key()
    {
        // A prop is put back to within a float, and nothing should lose its
        // number over the last decimal place.
        Assert.Equal(
            PluginWorld.PlaceOf(At(4f, 0f, -12f)),
            PluginWorld.PlaceOf(At(4.04f, 0.03f, -11.98f)));
    }

    [Fact]
    public void Two_phones_on_one_wall_are_two_places()
    {
        Assert.NotEqual(PluginWorld.PlaceOf(At(4f, 0f, -12f)), PluginWorld.PlaceOf(At(5f, 0f, -12f)));
    }

    [Fact]
    public void Height_counts_as_much_as_the_floor_does()
        => Assert.NotEqual(PluginWorld.PlaceOf(At(4f, 0f, -12f)), PluginWorld.PlaceOf(At(4f, 3f, -12f)));

    [Fact]
    public void A_grid_of_nothing_falls_back_rather_than_dividing_by_it()
        => Assert.NotEmpty(PluginWorld.PlaceOf(At(4f, 0f, -12f), 0f));

    [Fact]
    public void Nothing_is_found_when_there_is_no_server_behind_it()
        => Assert.Null(new PluginWorld().Find(300));

    [Fact]
    public void What_the_server_answers_comes_straight_back()
    {
        var world = new PluginWorld { Lookup = id => id == 300 ? At(1f, 2f, 3f) : null };

        Assert.Equal("Pack.Spawnable.Payphone", world.Find(300)!.Value.Barcode);
        Assert.Null(world.Find(301));
    }
}
