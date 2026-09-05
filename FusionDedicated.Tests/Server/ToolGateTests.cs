using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The dev tool settings used to be advertised to clients and never checked, so a
/// guest could pick up a tool the server said was operator only.
/// </summary>
public class ToolGateTests
{
    private static ToolGates Gates(
        PermissionLevel devTools = PermissionLevel.Default,
        PermissionLevel constrainer = PermissionLevel.Default,
        PermissionLevel nimbus = PermissionLevel.Default)
        => new(devTools, constrainer, nimbus);

    [Theory]
    [InlineData("SLZ.BONELAB.Content.Spawnable.NimbusGun", ToolFamily.Nimbus)]
    [InlineData("somemod.spawnable.nimbus_gun", ToolFamily.Nimbus)]
    [InlineData("SLZ.BONELAB.Content.Spawnable.Constrainer", ToolFamily.Constrainer)]
    [InlineData("SLZ.BONELAB.Content.Spawnable.SpawnGun", ToolFamily.DevTools)]
    public void A_tool_is_recognised_by_its_name(string barcode, ToolFamily expected)
    {
        Assert.Equal(expected, ToolGate.Family(barcode));
    }

    [Theory]
    [InlineData("c1534c5a-6b38-438a-a324-d7e147616467", ToolFamily.Nimbus)]
    [InlineData("c1534c5a-3813-49d6-a98c-f595436f6e73", ToolFamily.Constrainer)]
    [InlineData("c1534c5a-5747-42a2-bd08-ab3b47616467", ToolFamily.DevTools)]
    [InlineData("c1534c5a-c6a8-45d0-aaa2-2c954465764d", ToolFamily.DevTools)]
    [InlineData("c1534c5a-e777-4d15-b0c1-3195426f6172", ToolFamily.DevTools)]
    public void The_base_game_tools_are_known_by_barcode(string barcode, ToolFamily expected)
    {
        // These carry no readable name, so a keyword match would never see them.
        Assert.Equal(expected, ToolGate.Family(barcode));
    }

    [Fact]
    public void A_guest_is_refused_the_real_nimbus_gun()
    {
        Assert.True(ToolGate.Check("c1534c5a-6b38-438a-a324-d7e147616467",
            PermissionLevel.Default, Gates(nimbus: PermissionLevel.Operator)).Blocked);
    }

    [Theory]
    [InlineData("SLZ.BONELAB.Content.Spawnable.Crate")]
    [InlineData("")]
    [InlineData("SLZ.BONELAB.Content.Avatar.Ford")]
    public void Anything_else_belongs_to_no_family(string barcode)
    {
        Assert.Equal(ToolFamily.None, ToolGate.Family(barcode));
    }

    [Fact]
    public void A_guest_is_refused_a_tool_kept_for_operators()
    {
        var verdict = ToolGate.Check(
            "SLZ.BONELAB.Content.Spawnable.NimbusGun",
            PermissionLevel.Default,
            Gates(nimbus: PermissionLevel.Operator));

        Assert.True(verdict.Blocked);
        Assert.Equal("tool-gate", verdict.Layer);
    }

    [Fact]
    public void An_operator_is_allowed_a_tool_kept_for_operators()
    {
        Assert.False(ToolGate.Check(
            "SLZ.BONELAB.Content.Spawnable.NimbusGun",
            PermissionLevel.Operator,
            Gates(nimbus: PermissionLevel.Operator)).Blocked);
    }

    [Fact]
    public void Each_family_is_gated_by_its_own_setting()
    {
        var onlyConstrainer = Gates(constrainer: PermissionLevel.Operator);

        Assert.True(ToolGate.Check(
            "SLZ.BONELAB.Content.Spawnable.Constrainer",
            PermissionLevel.Default, onlyConstrainer).Blocked);

        Assert.False(ToolGate.Check(
            "SLZ.BONELAB.Content.Spawnable.SpawnGun",
            PermissionLevel.Default, onlyConstrainer).Blocked);
    }

    [Fact]
    public void Restricting_dev_tools_restricts_the_nimbus_gun_with_them()
    {
        // The base game tags it a dev tool and Fusion has no setting of its own for
        // it, so an owner who restricts dev tools expects it covered.
        Assert.True(ToolGate.Check("c1534c5a-6b38-438a-a324-d7e147616467",
            PermissionLevel.Default, Gates(devTools: PermissionLevel.Operator)).Blocked);
    }

    [Fact]
    public void The_nimbus_setting_can_go_stricter_than_dev_tools_but_not_looser()
    {
        Assert.True(ToolGate.Check("c1534c5a-6b38-438a-a324-d7e147616467",
            PermissionLevel.Operator,
            Gates(devTools: PermissionLevel.Default, nimbus: PermissionLevel.Owner)).Blocked);

        Assert.True(ToolGate.Check("c1534c5a-6b38-438a-a324-d7e147616467",
            PermissionLevel.Default,
            Gates(devTools: PermissionLevel.Operator, nimbus: PermissionLevel.Default)).Blocked);
    }

    [Fact]
    public void A_gate_left_at_default_lets_everyone_through()
    {
        Assert.False(ToolGate.Check(
            "SLZ.BONELAB.Content.Spawnable.NimbusGun",
            PermissionLevel.Default, Gates()).Blocked);
    }

    [Fact]
    public void A_guest_is_below_default_and_still_refused()
    {
        Assert.True(ToolGate.Check(
            "SLZ.BONELAB.Content.Spawnable.SpawnGun",
            PermissionLevel.Guest,
            Gates(devTools: PermissionLevel.Default)).Blocked);
    }

    [Fact]
    public void An_entity_the_server_never_saw_spawned_has_no_barcode_to_judge()
    {
        // NotePose registers a discovered entity with an empty barcode. Refusing
        // those would stop players picking up ordinary scene props.
        Assert.False(ToolGate.Check("", PermissionLevel.Guest,
            Gates(devTools: PermissionLevel.Owner)).Blocked);
    }
}
