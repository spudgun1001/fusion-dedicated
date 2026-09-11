using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A client never asks about a prop it owns, so its values are sent again once it
/// has loaded. After a restart the first player owns every kept payphone, and the
/// numbers never showed.
/// </summary>
public class OwnVariablesTests
{
    [Fact]
    public void A_prop_the_player_owns_is_resent()
        => Assert.Equal(new ushort[] { 300 }, WorldCatchup.OwnedBy(new (ushort, byte?)[] { (300, 1) }, 1));

    [Fact]
    public void A_prop_somebody_else_owns_is_not()
        => Assert.Empty(WorldCatchup.OwnedBy(new (ushort, byte?)[] { (300, 2) }, 1));

    [Fact]
    public void A_prop_nobody_owns_is_not()
        => Assert.Empty(WorldCatchup.OwnedBy(new (ushort, byte?)[] { (300, null) }, 1));

    [Fact]
    public void Finishing_loading_resends_values_on_props_the_player_owns()
        => Assert.Contains("ResendOwnVariables(sender, AfterLoadingDelays);",
            FusionServerSource.Method("private void HandleMetadataRequest("));

    [Fact]
    public void The_resend_replays_each_prop_the_player_owns()
    {
        string method = FusionServerSource.Method("private void ResendOwnVariables(");

        Assert.Contains("WorldCatchup.OwnedBy(", method);
        Assert.Contains("ReplayVariables(player, ", method);
    }
}
