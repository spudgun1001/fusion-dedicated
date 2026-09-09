namespace FusionDedicated.Tests.Web;

/// <summary>
/// The world list has to say whether a magazine is in a gun.
///
/// Its position cannot: a magazine that is loaded goes kinematic and stops
/// sending poses, so what the panel shows is wherever it was last simulated,
/// usually metres from the gun holding it. Two operators in a row have looked at
/// that list, seen a magazine sitting nowhere near its gun, and reasonably feared
/// the ammo cull was about to take a loaded one.
/// </summary>
public class EntityAmmoChipTests
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
    public void The_world_list_says_when_something_is_held_in_use()
    {
        Assert.Contains("in use", Page(), StringComparison.Ordinal);
    }

    [Fact]
    public void It_says_when_a_magazine_is_loose_and_therefore_on_the_clock()
    {
        Assert.Contains("loose", Page(), StringComparison.Ordinal);
    }

    [Fact]
    public void It_reads_both_from_the_row_rather_than_guessing_from_position()
    {
        string page = Page();

        Assert.Contains("e.attached", page, StringComparison.Ordinal);
        Assert.Contains("e.ammo", page, StringComparison.Ordinal);
    }
}
