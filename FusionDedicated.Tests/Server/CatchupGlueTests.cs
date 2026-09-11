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
}
