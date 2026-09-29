using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class PanelUsersTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-users-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "panel-users.json");

    public PanelUsersTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void A_password_is_never_stored_as_itself()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Save();

        string onDisk = File.ReadAllText(Path_);

        Assert.DoesNotContain("hunter2", onDisk);
    }

    [Fact]
    public void The_right_password_is_accepted_and_carries_its_role()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Moderator);

        Assert.Equal(PanelRole.Moderator, users.Authenticate("badger", "hunter2"));
    }

    [Fact]
    public void A_wrong_password_is_refused()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);

        Assert.Null(users.Authenticate("badger", "hunter3"));
    }

    [Fact]
    public void An_unknown_account_is_refused()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);

        Assert.Null(users.Authenticate("nobody", "hunter2"));
    }

    [Fact]
    public void An_account_name_is_matched_whatever_its_case()
    {
        var users = new PanelUsers(Path_);
        users.Set("Badger", "hunter2", PanelRole.Owner);

        Assert.Equal(PanelRole.Owner, users.Authenticate("badger", "hunter2"));
    }

    [Fact]
    public void Two_accounts_with_one_password_get_different_hashes()
    {
        var users = new PanelUsers(Path_);
        users.Set("a", "same", PanelRole.Viewer);
        users.Set("b", "same", PanelRole.Viewer);
        users.Save();

        string onDisk = File.ReadAllText(Path_);
        var hashes = onDisk.Split("\"hash\"").Skip(1)
            .Select(s => s.Split('"')[1]).ToList();

        Assert.Equal(2, hashes.Count);
        Assert.NotEqual(hashes[0], hashes[1]);
    }

    [Fact]
    public void Accounts_survive_a_restart()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Moderator);
        users.Save();

        var reloaded = new PanelUsers(Path_);
        reloaded.Load();

        Assert.Equal(PanelRole.Moderator, reloaded.Authenticate("badger", "hunter2"));
    }

    [Fact]
    public void Removing_an_account_refuses_it()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);

        Assert.True(users.Remove("badger"));
        Assert.Null(users.Authenticate("badger", "hunter2"));
    }

    [Fact]
    public void A_file_that_will_not_parse_keeps_the_accounts_already_loaded()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Save();

        File.WriteAllText(Path_, "{ not json");
        users.Load();

        Assert.Equal(PanelRole.Owner, users.Authenticate("badger", "hunter2"));
    }

    [Fact]
    public void An_empty_password_is_never_accepted()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);

        Assert.Null(users.Authenticate("badger", ""));
    }

    [Fact]
    public void A_second_sign_in_with_the_same_password_skips_the_hash()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Moderator);

        users.Authenticate("badger", "hunter2");
        int after = users.Derivations;

        Assert.Equal(PanelRole.Moderator, users.Authenticate("badger", "hunter2"));
        Assert.Equal(after, users.Derivations);
    }

    [Fact]
    public void A_wrong_password_is_never_remembered()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Authenticate("badger", "hunter2");

        Assert.Null(users.Authenticate("badger", "hunter3"));
        int after = users.Derivations;
        Assert.Null(users.Authenticate("badger", "hunter3"));
        Assert.Equal(after + 1, users.Derivations);
    }

    [Fact]
    public void A_changed_password_refuses_the_remembered_old_one()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Authenticate("badger", "hunter2");

        users.Set("badger", "fresh", PanelRole.Owner);

        Assert.Null(users.Authenticate("badger", "hunter2"));
        Assert.Equal(PanelRole.Owner, users.Authenticate("badger", "fresh"));
    }

    [Fact]
    public void A_removed_account_is_refused_even_when_remembered()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Authenticate("badger", "hunter2");

        users.Remove("badger");

        Assert.Null(users.Authenticate("badger", "hunter2"));
    }

    [Fact]
    public void A_remembered_password_is_hashed_again_after_five_minutes()
    {
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var users = new PanelUsers(Path_) { Clock = () => now };
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Authenticate("badger", "hunter2");
        int after = users.Derivations;

        now = now.AddMinutes(4);
        users.Authenticate("badger", "hunter2");
        Assert.Equal(after, users.Derivations);

        now = now.AddMinutes(1).AddSeconds(1);
        Assert.Equal(PanelRole.Owner, users.Authenticate("badger", "hunter2"));
        Assert.Equal(after + 1, users.Derivations);
    }

    [Fact]
    public void A_clock_moved_backwards_hashes_again()
    {
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var users = new PanelUsers(Path_) { Clock = () => now };
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Authenticate("badger", "hunter2");
        int after = users.Derivations;

        now = now.AddHours(-1);

        Assert.Equal(PanelRole.Owner, users.Authenticate("badger", "hunter2"));
        Assert.Equal(after + 1, users.Derivations);
    }

    [Fact]
    public void Removing_or_changing_an_account_forgets_its_password()
    {
        var users = new PanelUsers(Path_);
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Authenticate("badger", "hunter2");
        Assert.True(users.Remembers("badger"));

        users.Set("badger", "fresh", PanelRole.Owner);
        Assert.False(users.Remembers("badger"));

        users.Authenticate("badger", "fresh");
        users.Remove("badger");
        Assert.False(users.Remembers("badger"));
    }

    [Fact]
    public void An_expired_entry_is_dropped_when_found()
    {
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var users = new PanelUsers(Path_) { Clock = () => now };
        users.Set("badger", "hunter2", PanelRole.Owner);
        users.Authenticate("badger", "hunter2");

        now = now.AddMinutes(6);
        users.Authenticate("badger", "wrong");

        Assert.False(users.Remembers("badger"));
    }
}
