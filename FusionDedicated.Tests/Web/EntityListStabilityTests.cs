namespace FusionDedicated.Tests.Web;

/// <summary>
/// The World tab's entity list is fetched every second. Rows sorted by last update jumped about and
/// the table was rebuilt under the pointer, so Keep could not be clicked.
/// </summary>
public class EntityListStabilityTests
{
    private static string Page()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "FusionDedicated", "Web", "index.html");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("index.html was not found above the test output");
    }

    [Fact]
    public void Entities_are_listed_newest_id_first_rather_than_by_last_update()
    {
        string source = DashboardSource.Text();

        Assert.Contains(".OrderByDescending(e => e.Id)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderByDescending(e => e.LastUpdate)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_is_not_redrawn_while_the_pointer_is_over_it()
    {
        string page = Page();

        Assert.Contains("addEventListener('pointerenter'", page, StringComparison.Ordinal);
        Assert.Contains("addEventListener('pointerleave'", page, StringComparison.Ordinal);
        Assert.Contains("if (worldHeld && !worldForce) return;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unchanged_list_is_not_redrawn()
        => Assert.Contains("if (html === worldHtml) return;", Page(), StringComparison.Ordinal);

    [Fact]
    public void Keep_drop_and_remove_redraw_the_list_straight_away()
    {
        string page = Page();
        int persist = page.IndexOf("async function persist(", StringComparison.Ordinal);
        int despawn = page.IndexOf("async function despawn(", StringComparison.Ordinal);

        Assert.True(persist >= 0 && despawn >= 0);
        Assert.Contains("worldForce = true;", page.Substring(persist, 300), StringComparison.Ordinal);
        Assert.Contains("worldForce = true;", page.Substring(despawn, 300), StringComparison.Ordinal);
    }
}
