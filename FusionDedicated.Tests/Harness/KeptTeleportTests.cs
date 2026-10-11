using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server.Props;
using FusionDedicated.Web;
using Steamworks;

namespace FusionDedicated.Tests.Harness;

/// <summary>The World page's kept props table, and sending staff to a kept prop.</summary>
public class KeptTeleportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-kept-teleport-" + Guid.NewGuid().ToString("N"));

    public KeptTeleportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static List<Vec3> TeleportsTo(World world, HSteamNetConnection connection)
        => world.Transport.SentTo(connection)
            .Where(s => s.Message[0] == GateProtocol.TagPlayerRepTeleport)
            .Select(s =>
            {
                var reader = new FusionNetReader(s.Message);
                reader.ReadByte();
                reader.ReadByte();
                reader.ReadByte();
                reader.ReadNullableByte();
                reader.ReadInt32();
                return new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            })
            .ToList();

    [Fact]
    public void Every_kept_prop_is_listed_past_the_recent_cap_with_its_note()
    {
        using var world = new World();
        world.Server.Props = new PersistentPropStore(Path.Combine(_dir, "persistent.json"));
        var owner = world.Join(1001, "Owner");

        for (ushort id = 1; id <= 70; id++)
        {
            world.Server.Entities.Register(id, "Pack.Spawnable.Crate", owner.SmallId, id * 3f, 0, 0);
            Assert.True(world.Server.KeepProp(id, "note " + id));
        }

        world.Server.Entities.Register(200, "Pack.Spawnable.Gun", owner.SmallId, 0, 0, 50);

        var kept = world.Server.KeptProps();

        Assert.Equal(70, kept.Count);
        Assert.DoesNotContain(kept, k => k.Entity.Id == 200);
        Assert.Equal("note 65", kept.Single(k => k.Entity.Id == 65).Note);
    }

    [Fact]
    public void Two_close_kept_props_get_the_notes_the_catch_up_links_them_to()
    {
        // Each one alone is nearest record B. The catch-up goes record first and claims, so A takes 1 and B takes 2.
        using var world = new World();
        var store = new PersistentPropStore(Path.Combine(_dir, "persistent.json"));
        string level = world.Server.Config.LevelBarcode;
        store.Add(new PersistentProp { Barcode = "Pack.Spawnable.Crate", Level = level, X = 0f, Note = "A" });
        store.Add(new PersistentProp { Barcode = "Pack.Spawnable.Crate", Level = level, X = 0.4f, Note = "B" });
        world.Server.Props = store;

        foreach (var (id, x) in new[] { ((ushort)1, 0.3f), ((ushort)2, 0.35f) })
        {
            var entity = world.Server.Entities.Register(id, "Pack.Spawnable.Crate", 1, x, 0, 0);
            entity.Persistent = true;
            entity.KeptAt = (x, 0f, 0f);
        }

        var kept = world.Server.KeptProps();

        Assert.Equal("A", kept.Single(k => k.Entity.Id == 1).Note);
        Assert.Equal("B", kept.Single(k => k.Entity.Id == 2).Note);
    }

    [Fact]
    public void Staff_lists_only_operators_and_owners_online()
    {
        using var world = new World();
        var op = world.Join(1001, "Op");
        var owner = world.Join(1002, "Boss");
        var player = world.Join(1003, "Player");

        world.Server.Players.Get(op.SmallId)!.Permission = PermissionLevel.Operator;
        world.Server.Players.Get(owner.SmallId)!.Permission = PermissionLevel.Owner;
        world.Server.Players.Get(player.SmallId)!.Permission = PermissionLevel.Default;

        Assert.Equal(new[] { op.SmallId, owner.SmallId },
            world.Server.OnlineStaff().Select(p => p.SmallId).OrderBy(s => s));
    }

    [Fact]
    public void A_staff_member_is_sent_once_to_one_metre_above_the_prop()
    {
        using var world = new World();
        var op = world.Join(1001, "Op");
        var other = world.Join(1002, "Other");
        world.Server.Players.Get(op.SmallId)!.Permission = PermissionLevel.Operator;
        world.Server.Entities.Register(40, "Pack.Spawnable.Crate", other.SmallId, 5f, 2f, -3f);

        Assert.Null(world.Server.TeleportStaffTo(op.SmallId, 40, "admin"));

        Assert.Equal(new[] { new Vec3(5f, 3f, -3f) }, TeleportsTo(world, op.Connection));
        Assert.Empty(TeleportsTo(world, other.Connection));
    }

    [Fact]
    public void A_teleport_is_refused_for_a_non_staff_or_unknown_player_or_entity()
    {
        using var world = new World();
        var op = world.Join(1001, "Op");
        var player = world.Join(1002, "Player");
        world.Server.Players.Get(op.SmallId)!.Permission = PermissionLevel.Operator;
        world.Server.Entities.Register(40, "Pack.Spawnable.Crate", player.SmallId, 5f, 2f, -3f);

        Assert.NotNull(world.Server.TeleportStaffTo(player.SmallId, 40, "admin"));
        Assert.NotNull(world.Server.TeleportStaffTo(250, 40, "admin"));
        Assert.NotNull(world.Server.TeleportStaffTo(op.SmallId, 999, "admin"));

        Assert.Empty(TeleportsTo(world, op.Connection));
        Assert.Empty(TeleportsTo(world, player.Connection));
    }

    [Fact]
    public void Teleporting_from_the_panel_needs_a_moderator()
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/teleport"));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/teleport"));
        Assert.True(PanelPermissions.Allows(PanelRole.Owner, "/api/teleport"));
    }
}
