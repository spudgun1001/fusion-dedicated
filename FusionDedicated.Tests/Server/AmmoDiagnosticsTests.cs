using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Holstered magazines were disappearing and the log could not say why: whether
/// the server knew they were attached, who owned them, or how long they had been
/// silent. These lines are for the detailed log only.
/// </summary>
public class AmmoDiagnosticsTests
{
    private const string Magazine = "c1534c5a-18ce-44aa-a416-63174d616761";
    private const string Gun = "Rexmeck.GLOCK17.Spawnable.GLOCK17";

    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private static TrackedEntity Culled(string barcode) => new()
    {
        Id = 300,
        Barcode = barcode,
        OwnerSmallId = 3,
        Source = 2,
        LastUpdate = Now.AddSeconds(-130),
        OwnerDistanceAtPose = 4.5f,
    };

    [Fact]
    public void A_culled_magazine_says_why()
    {
        string? line = AmmoDiagnostics.DescribeCull(Culled(Magazine), ownerOnline: true, Now);

        Assert.Equal(
            "Ammo culled: UMP Mag (entity 300), attached no, source 2, owner 3 (online), " +
            "inherited no, 130s since last pose, owner distance at pose 4.5 m, culled for owner no",
            line);
    }

    [Fact]
    public void A_modded_magazine_is_named_by_its_crate()
    {
        string? line = AmmoDiagnostics.DescribeCull(Culled("SomePack.Spawnable.MagAK47"), true, Now);

        Assert.StartsWith("Ammo culled: MagAK47 (entity 300)", line);
    }

    [Fact]
    public void A_distance_nobody_measured_is_unknown()
    {
        var entity = Culled(Magazine);
        entity.OwnerDistanceAtPose = null;

        Assert.Contains("owner distance at pose unknown", AmmoDiagnostics.DescribeCull(entity, true, Now));
    }

    [Fact]
    public void An_owner_who_left_is_shown_offline()
    {
        Assert.Contains("owner 3 (offline)", AmmoDiagnostics.DescribeCull(Culled(Magazine), false, Now));
    }

    [Fact]
    public void An_orphaned_magazine_has_no_owner()
    {
        var entity = Culled(Magazine);
        entity.OwnerSmallId = null;

        Assert.Contains(", no owner,", AmmoDiagnostics.DescribeCull(entity, false, Now));
    }

    [Fact]
    public void The_flags_that_decide_its_clock_are_shown()
    {
        var entity = Culled(Magazine);
        entity.Attached = true;
        entity.Inherited = true;
        entity.CulledForOwner = true;

        string? line = AmmoDiagnostics.DescribeCull(entity, true, Now);

        Assert.Contains("attached yes", line);
        Assert.Contains("inherited yes", line);
        Assert.Contains("culled for owner yes", line);
    }

    [Fact]
    public void Something_that_is_not_ammo_is_not_described()
    {
        Assert.Null(AmmoDiagnostics.DescribeCull(Culled(Gun), true, Now));
    }

    [Fact]
    public void A_magazine_going_into_a_body_slot_is_named()
    {
        var change = new ModuleProtocol.AttachmentChange(
            ModuleProtocol.AttachmentKind.SlotInsert, 300, 3, 1);

        Assert.Equal(
            "Ammo into body slot 3 index 1: UMP Mag (entity 300)",
            AmmoDiagnostics.DescribeAttachment(change, Magazine, null));
    }

    [Fact]
    public void A_magazine_coming_out_of_a_body_slot_is_named_from_what_the_slot_held()
    {
        // The drop message names the slot, not what was in it.
        var change = new ModuleProtocol.AttachmentChange(
            ModuleProtocol.AttachmentKind.SlotDrop, 0, 3, 1);

        Assert.Equal(
            "Ammo out of body slot 3 index 1: UMP Mag (entity 300)",
            AmmoDiagnostics.DescribeAttachment(change, Magazine, 300));
    }

    [Fact]
    public void A_drop_from_a_slot_nobody_recorded_is_not_described()
    {
        var change = new ModuleProtocol.AttachmentChange(
            ModuleProtocol.AttachmentKind.SlotDrop, 0, 3, 1);

        Assert.Null(AmmoDiagnostics.DescribeAttachment(change, "", null));
    }

    [Fact]
    public void A_gun_going_into_a_holster_is_not_described()
    {
        var change = new ModuleProtocol.AttachmentChange(
            ModuleProtocol.AttachmentKind.SlotInsert, 300, 3, 1);

        Assert.Null(AmmoDiagnostics.DescribeAttachment(change, Gun, null));
    }

    [Fact]
    public void A_magazine_going_into_a_gun_is_not_described()
    {
        // Every reload would be a line.
        var change = new ModuleProtocol.AttachmentChange(
            ModuleProtocol.AttachmentKind.Attach, 300, 0, 0, 400);

        Assert.Null(AmmoDiagnostics.DescribeAttachment(change, Magazine, null));
    }

    [Fact]
    public void A_pose_remembers_how_far_its_sender_was()
    {
        var registry = new EntityRegistry();
        registry.Register(300, Magazine, 1, 0, 0, 0);

        registry.NotePose(300, 1, 0, 0, 0, ownerDistance: 2.5f);

        Assert.Equal((float?)2.5f, registry.Get(300)!.OwnerDistanceAtPose);
    }

    [Fact]
    public void A_pose_with_no_distance_forgets_the_old_one()
    {
        var registry = new EntityRegistry();
        registry.Register(300, Magazine, 1, 0, 0, 0);

        registry.NotePose(300, 1, 0, 0, 0, ownerDistance: 2.5f);
        registry.NotePose(300, 1, 0, 0, 0);

        Assert.Null(registry.Get(300)!.OwnerDistanceAtPose);
    }

    [Fact]
    public void A_discovered_entity_remembers_the_distance_too()
    {
        var registry = new EntityRegistry();

        registry.NotePose(500, 7, 0, 0, 0, ownerDistance: 3f);

        Assert.Equal((float?)3f, registry.Get(500)!.OwnerDistanceAtPose);
    }

    [Fact]
    public void The_detailed_cull_hands_back_what_it_removed()
    {
        var registry = new EntityRegistry();
        var created = registry.Register(300, Magazine, 1, 0, 0, 0);
        created.Source = FusionProtocol.SourceNone;
        created.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

        var removed = registry.CullStaleDetailed(
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15), TimeSpan.Zero, TimeSpan.FromMinutes(2));

        var entity = Assert.Single(removed);
        Assert.Equal((ushort)300, entity.Id);
        Assert.Equal(Magazine, entity.Barcode);
        Assert.Null(registry.Get(300));
    }

    [Fact]
    public void The_detailed_cull_still_announces_what_it_removed()
    {
        var registry = new EntityRegistry();
        var entity = registry.Register(300, Magazine, 1, 0, 0, 0);
        entity.Source = FusionProtocol.SourceNone;
        entity.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

        var announced = new List<ushort>();
        registry.Removed += announced.Add;

        registry.CullStaleDetailed(
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15), TimeSpan.Zero, TimeSpan.FromMinutes(2));

        Assert.Equal(new ushort[] { 300 }, announced);
    }
}
