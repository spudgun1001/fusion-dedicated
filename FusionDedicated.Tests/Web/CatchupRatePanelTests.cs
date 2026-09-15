namespace FusionDedicated.Tests.Web;

/// <summary>The panel shows and saves the catch-up pace, from 0 to 1000 like the flood limits.</summary>
public class CatchupRatePanelTests
{
    private static string Page() => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "FusionDedicated", "Web", "index.html"))
        .Replace("\r\n", "\n");

    [Fact]
    public void The_panel_state_carries_the_pace()
        => Assert.Contains("catchupMessagesPerSecond = _config.CatchupMessagesPerSecond,", DashboardSource.Text());

    [Fact]
    public void The_settings_take_the_pace_from_0_to_1000()
        => Assert.Contains(
            "query[\"catchupMessagesPerSecond\"], out var catchupRate) && catchupRate is >= 0 and <= 1000",
            DashboardSource.Method("private void HandleSettings("));

    [Fact]
    public void The_page_fills_and_saves_the_pace()
    {
        string page = Page();

        Assert.Contains("<input id=\"fCatchupRate\" type=\"number\" min=\"0\" max=\"1000\" data-s>", page);
        Assert.Contains("$('fCatchupRate').value = g.catchupMessagesPerSecond;", page);
        Assert.Contains("catchupMessagesPerSecond: $('fCatchupRate').value,", page);
    }
}
