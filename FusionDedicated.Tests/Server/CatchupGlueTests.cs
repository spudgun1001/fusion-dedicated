namespace FusionDedicated.Tests.Server;

/// <summary>
/// The server glue for held-item catch-up.
///
/// Read from the source because it needs live Steam connections, the same way
/// JoinOrderTests pins the join handshake.
/// </summary>
public class CatchupGlueTests
{
    private static string Source() => FusionServerSource.Text();

    private static string Method(string declaration) => FusionServerSource.Method(declaration);

    /// <summary>One case of the message switch, up to the next case.</summary>
    private static string Case(string label)
    {
        string handler = Method("private void HandleMessage(");
        int start = handler.IndexOf(label, StringComparison.Ordinal);

        Assert.True(start > 0, $"'{label}' moved");

        int end = handler.IndexOf("\n            case ", start + label.Length, StringComparison.Ordinal);

        return end > start ? handler[start..end] : handler[start..];
    }

    [Fact]
    public void Scene_props_name_their_owner_through_the_rule()
        => Assert.Contains("WorldCatchup.PropOwner(", Method("private int SendSceneProps("));

    [Theory]
    [InlineData("case FusionProtocol.TagPlayerRepGrab when sender != null:")]
    [InlineData("case FusionProtocol.TagPlayerRepRelease when sender != null:")]
    public void Grabs_and_releases_are_read_and_still_passed_on(string label)
    {
        // Other clients move the hand from these, so reading one must never stop it.
        string handled = Case(label);

        Assert.Contains("break;", handled);
        Assert.DoesNotContain("return;", handled);
    }

    [Fact]
    public void A_player_who_leaves_holds_nothing()
        => Assert.Contains("_grabs.ForgetPlayer(player.SmallId);", Method("private void Depart("));

    [Fact]
    public void A_new_avatar_holds_nothing()
        => Assert.Contains("_grabs.ForgetPlayer(sender.SmallId);",
            Case("case GateProtocol.TagPlayerRepAvatar when sender != null:"));

    [Fact]
    public void An_entity_that_leaves_the_books_is_let_go()
        => Assert.Contains("Entities.Removed += id => _grabs.ForgetEntity(id);", Source());

    [Fact]
    public void A_player_who_leaves_takes_their_holsters_with_them()
    {
        string depart = Method("private void Depart(");

        Assert.Contains("_slotted.ForgetRig(player.SmallId)", depart);
        Assert.Contains("Entities.SetAttached(weapon, false)", depart);
    }

    [Fact]
    public void A_held_gun_is_not_put_back_in_a_holster()
        => Assert.Contains("WorldCatchup.ShouldReseat(_grabs.HoldersOf(weapon))",
            Method("private int SendAttachments("));

    [Fact]
    public void A_data_request_is_handled_before_it_is_relayed()
        => Assert.Contains("HandleEntityDataRequest(sender, message);",
            Case("case FusionProtocol.TagEntityDataRequest when sender != null:"));

    [Fact]
    public void A_stale_request_tells_the_asker_and_asks_the_owner()
    {
        string handler = Method("private void HandleEntityDataRequest(");

        Assert.Contains("WorldCatchup.DataRequestTarget(", handler);
        Assert.Contains("FusionProtocol.BuildOwnershipResponse(owner, request.EntityId)", handler);
        Assert.Contains("FusionProtocol.BuildEntityDataRequest(sender.SmallId, owner, request.EntityId)", handler);
        Assert.Contains("Relay(sender, message);", handler);
    }

    [Fact]
    public void Owner_announcements_use_the_shared_builder()
    {
        string announce = Method("private void AnnounceOwner(");

        Assert.Contains("Broadcast(FusionProtocol.BuildOwnershipResponse(owner, entityId), reliable: true);", announce);
        Assert.DoesNotContain("new FusionNetWriter", announce);
    }
}
