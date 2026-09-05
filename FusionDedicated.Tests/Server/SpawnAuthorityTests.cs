using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The tool gate only knows dev tools, and the blocklist only knows barcodes it
/// has been told about, so a spawn menu mod could hand anyone a weapon. This is
/// the gate that asks who is allowed to spawn at all.
/// </summary>
public class SpawnAuthorityTests
{
    [Fact]
    public void Everyone_may_spawn_when_the_gate_is_left_at_default()
    {
        Assert.False(SpawnAuthority.Check(PermissionLevel.Default, PermissionLevel.Default).Blocked);
    }

    [Fact]
    public void A_default_player_is_refused_when_spawning_is_kept_for_operators()
    {
        var verdict = SpawnAuthority.Check(PermissionLevel.Default, PermissionLevel.Operator);

        Assert.True(verdict.Blocked);
        Assert.Equal("rank", verdict.Layer);
    }

    [Fact]
    public void An_operator_may_still_spawn_when_spawning_is_kept_for_operators()
    {
        Assert.False(SpawnAuthority.Check(PermissionLevel.Operator, PermissionLevel.Operator).Blocked);
    }

    [Fact]
    public void An_owner_outranks_the_gate()
    {
        Assert.False(SpawnAuthority.Check(PermissionLevel.Owner, PermissionLevel.Operator).Blocked);
    }

    [Fact]
    public void A_guest_sits_below_default_and_can_be_held_back_on_an_open_server()
    {
        Assert.True(SpawnAuthority.Check(PermissionLevel.Guest, PermissionLevel.Default).Blocked);
        Assert.False(SpawnAuthority.Check(PermissionLevel.Guest, PermissionLevel.Guest).Blocked);
    }

    [Fact]
    public void The_reason_names_the_rank_that_was_needed()
    {
        // Ranks are spelled the way Fusion spells them on the wire, which is upper case.
        Assert.Contains("operator",
            SpawnAuthority.Check(PermissionLevel.Guest, PermissionLevel.Operator).Reason,
            StringComparison.OrdinalIgnoreCase);
    }
}
