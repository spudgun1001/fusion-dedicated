namespace FusionDedicated.Tests.Web;

/// <summary>The panel shows and saves the three flood limits, each from 0 to 1000.</summary>
public class FloodLimitPanelTests
{
    private static string Page() => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "FusionDedicated", "Web", "index.html"))
        .Replace("\r\n", "\n");

    [Fact]
    public void The_panel_state_carries_the_three_limits()
    {
        string source = DashboardSource.Text();

        Assert.Contains("metadataPerSecond = _config.MetadataPerSecond,", source);
        Assert.Contains("avatarSwapsPerSecond = _config.AvatarSwapsPerSecond,", source);
        Assert.Contains("rpcMessagesPerSecond = _config.RpcMessagesPerSecond,", source);
    }

    [Fact]
    public void The_settings_take_each_limit_from_0_to_1000()
    {
        string settings = DashboardSource.Method("private void HandleSettings(");

        Assert.Contains("query[\"metadataPerSecond\"], out var metadataRate) && metadataRate is >= 0 and <= 1000", settings);
        Assert.Contains("query[\"avatarSwapsPerSecond\"], out var avatarRate) && avatarRate is >= 0 and <= 1000", settings);
        Assert.Contains("query[\"rpcMessagesPerSecond\"], out var rpcRate) && rpcRate is >= 0 and <= 1000", settings);
    }

    [Fact]
    public void The_page_fills_and_saves_the_three_limits()
    {
        string page = Page();

        Assert.Contains("<input id=\"fMetadataRate\" type=\"number\" min=\"0\" max=\"1000\" data-s>", page);
        Assert.Contains("<input id=\"fAvatarRate\" type=\"number\" min=\"0\" max=\"1000\" data-s>", page);
        Assert.Contains("<input id=\"fRpcRate\" type=\"number\" min=\"0\" max=\"1000\" data-s>", page);

        Assert.Contains("$('fMetadataRate').value = g.metadataPerSecond;", page);
        Assert.Contains("$('fAvatarRate').value = g.avatarSwapsPerSecond;", page);
        Assert.Contains("$('fRpcRate').value = g.rpcMessagesPerSecond;", page);

        Assert.Contains("metadataPerSecond: $('fMetadataRate').value,", page);
        Assert.Contains("avatarSwapsPerSecond: $('fAvatarRate').value,", page);
        Assert.Contains("rpcMessagesPerSecond: $('fRpcRate').value,", page);
    }
}
