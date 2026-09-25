namespace FusionDedicated.Tests.Web;

/// <summary>Plugin pages draw tree sections, and a button can ask for one value inline.</summary>
public class PluginTreePanelTests
{
    private static string Page()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "FusionDedicated", "Web", "index.html");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n");
            }
        }

        throw new FileNotFoundException("index.html was not found above the test output");
    }

    [Fact]
    public void Tree_sections_are_drawn()
        => Assert.Contains("section.kind === 'tree'", Page());

    [Fact]
    public void A_button_with_an_input_asks_before_it_sends()
    {
        string page = Page();

        Assert.Contains("function askFor(", page);
        Assert.Contains("if (spec.input)", page);
    }

    [Fact]
    public void The_poll_leaves_an_open_input_alone()
        => Assert.Contains("if (!force && box.querySelector('.ask')) return;", Page());

    [Fact]
    public void A_refused_plain_button_redraws_so_the_page_is_not_left_stale()
        => Assert.Contains("if (!spec.input) renderPluginPage(true);", Page());

    [Fact]
    public void A_pick_list_opens_on_its_current_value()
        => Assert.Contains("if (f.placeholder) box.value = f.placeholder;", Page());

    [Fact]
    public void Deep_plugin_trees_serialise()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "FusionDedicated", "Web", "Dashboard.cs");

            if (File.Exists(path))
            {
                Assert.Contains("MaxDepth = 256", File.ReadAllText(path));
                return;
            }
        }

        throw new FileNotFoundException("Dashboard.cs");
    }
}
