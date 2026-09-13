namespace FusionDedicated.Tests.Web;

/// <summary>The panel's Keep button on a prop that is already kept.</summary>
public class PanelKeepTests
{
    [Fact]
    public void The_panel_says_when_a_prop_is_already_kept()
    {
        string persist = DashboardSource.Method("private void HandlePersist(");

        Assert.Contains("That prop is already kept. Drop it first to keep it somewhere else.", persist);
        Assert.Contains("That prop is no longer in the world.", persist);
    }
}
