using FusionDedicated.Server.Bans;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The reason on a ban is what the banned player is shown, so it cannot hold what
/// an admin wants to say to the other admins. The note is that, and it can be
/// written after the ban, since the story is usually longer than the moment.
/// </summary>
public class BanNoteTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-bannote-" + Guid.NewGuid().ToString("N"));

    private BanStore Store()
    {
        Directory.CreateDirectory(_dir);
        return new BanStore(Path.Combine(_dir, "bans.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void A_ban_starts_with_no_note()
    {
        var store = Store();
        store.Ban(1, "griefer", "Spawn nuke");

        Assert.Equal("", store.Find(1)!.Note);
    }

    [Fact]
    public void A_note_can_be_written_after_the_ban()
    {
        var store = Store();
        store.Ban(1, "griefer", "Spawn nuke");

        Assert.True(store.SetNote(1, "Third time. Do not unban without asking."));
        Assert.Equal("Third time. Do not unban without asking.", store.Find(1)!.Note);
    }

    [Fact]
    public void A_note_on_somebody_who_is_not_banned_is_refused()
    {
        Assert.False(Store().SetNote(999, "anything"));
    }

    [Fact]
    public void A_note_does_not_disturb_the_reason_shown_to_the_player()
    {
        var store = Store();
        store.Ban(1, "griefer", "Spawn nuke");
        store.SetNote(1, "Alt of an earlier ban");

        Assert.Equal("Spawn nuke", store.Find(1)!.Reason);
    }

    [Fact]
    public void A_note_survives_being_written_and_read_back()
    {
        string path = Path.Combine(_dir, "bans.json");
        Directory.CreateDirectory(_dir);

        var store = new BanStore(path);
        store.Ban(1, "griefer", "Spawn nuke");
        store.SetNote(1, "Caught on the second map");
        store.Save();

        var reopened = new BanStore(path);
        reopened.Load();

        Assert.Equal("Caught on the second map", reopened.Find(1)!.Note);
    }

    [Fact]
    public void A_note_can_be_cleared()
    {
        var store = Store();
        store.Ban(1, "griefer", "Spawn nuke");
        store.SetNote(1, "something");

        Assert.True(store.SetNote(1, ""));
        Assert.Equal("", store.Find(1)!.Note);
    }

    [Fact]
    public void Rebanning_somebody_keeps_the_note_already_written_about_them()
    {
        var store = Store();
        store.Ban(1, "griefer", "Spawn nuke");
        store.SetNote(1, "Alt of an earlier ban");

        store.Ban(1, "griefer", "Came back");

        Assert.Equal("Alt of an earlier ban", store.Find(1)!.Note);
    }
}
