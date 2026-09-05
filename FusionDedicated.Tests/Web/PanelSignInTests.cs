using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>
/// The panel account from the egg is always an owner, so a broken accounts file
/// or a forgotten password cannot shut everyone out of a running server.
/// </summary>
public class PanelSignInTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-signin-" + Guid.NewGuid().ToString("N"));

    private PanelUsers Users()
    {
        Directory.CreateDirectory(_dir);
        return new PanelUsers(Path.Combine(_dir, "panel-users.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string Basic(string user, string password)
    {
        var raw = System.Text.Encoding.UTF8.GetBytes(user + ":" + password);
        return "Basic " + Convert.ToBase64String(raw);
    }

    [Fact]
    public void The_configured_account_signs_in_as_owner()
    {
        var role = PanelSignIn.Resolve(
            Basic("admin", "letmein"), "admin", "letmein", Users());

        Assert.Equal(PanelRole.Owner, role);
    }

    [Fact]
    public void The_configured_account_is_owner_even_when_the_file_demotes_it()
    {
        var users = Users();
        users.Set("admin", "letmein", PanelRole.Viewer);

        Assert.Equal(PanelRole.Owner,
            PanelSignIn.Resolve(Basic("admin", "letmein"), "admin", "letmein", users));
    }

    [Fact]
    public void An_account_from_the_file_signs_in_with_its_own_role()
    {
        var users = Users();
        users.Set("badger", "hunter2", PanelRole.Moderator);

        Assert.Equal(PanelRole.Moderator,
            PanelSignIn.Resolve(Basic("badger", "hunter2"), "admin", "letmein", users));
    }

    [Fact]
    public void A_wrong_password_gets_nothing()
    {
        var users = Users();
        users.Set("badger", "hunter2", PanelRole.Moderator);

        Assert.Null(PanelSignIn.Resolve(Basic("badger", "wrong"), "admin", "letmein", users));
        Assert.Null(PanelSignIn.Resolve(Basic("admin", "wrong"), "admin", "letmein", users));
    }

    [Fact]
    public void No_header_gets_nothing()
    {
        Assert.Null(PanelSignIn.Resolve(null, "admin", "letmein", Users()));
        Assert.Null(PanelSignIn.Resolve("", "admin", "letmein", Users()));
    }

    [Fact]
    public void Rubbish_in_the_header_gets_nothing()
    {
        Assert.Null(PanelSignIn.Resolve("Basic !!!not base64!!!", "admin", "letmein", Users()));
        Assert.Null(PanelSignIn.Resolve("Bearer abc", "admin", "letmein", Users()));
    }

    [Fact]
    public void An_unset_panel_password_leaves_the_panel_open_as_it_did_before()
    {
        // Matches the existing behaviour for a panel bound to loopback with no
        // password, which Start refuses to expose on any other interface.
        Assert.Equal(PanelRole.Owner, PanelSignIn.Resolve(null, "admin", "", Users()));
    }

    [Fact]
    public void An_unset_panel_password_still_lets_a_named_account_be_itself()
    {
        var users = Users();
        users.Set("badger", "hunter2", PanelRole.Viewer);

        Assert.Equal(PanelRole.Viewer,
            PanelSignIn.Resolve(Basic("badger", "hunter2"), "admin", "", users));
    }
}
