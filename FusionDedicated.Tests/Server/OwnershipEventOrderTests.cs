namespace FusionDedicated.Tests.Server;

/// <summary>
/// A plugin has to be asked after the built-in MayHold check, and a plugin refusal
/// has to stop the request before it commits. Read from the source (Task 1's
/// FusionServerSource) because the method needs live connections to call.
/// </summary>
public class OwnershipEventOrderTests
{
    private static string OwnershipHandshake()
        => FusionServerSource.Method("private void HandleOwnershipRequest(");

    [Fact]
    public void The_plugin_event_is_raised_after_MayHold()
    {
        string handshake = OwnershipHandshake();

        int mayHold = handshake.IndexOf("MayHold(sender, entityId)", StringComparison.Ordinal);
        int raised = handshake.IndexOf("Plugins?.Ownership.Raise", StringComparison.Ordinal);

        Assert.True(mayHold > 0, "MayHold is no longer checked here");
        Assert.True(raised > mayHold, "the ownership event is raised before MayHold decides");
    }

    [Fact]
    public void A_refusal_returns_before_setting_the_owner()
    {
        string handshake = OwnershipHandshake();

        int refused = handshake.IndexOf("Allowed: false", StringComparison.Ordinal);
        int setOwner = handshake.IndexOf("Entities.SetOwner(entityId, requestedOwner)", StringComparison.Ordinal);

        Assert.True(refused > 0 && refused < setOwner,
            "a plugin refusal must return before the owner is set");
    }
}
