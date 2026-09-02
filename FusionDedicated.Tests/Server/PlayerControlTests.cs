using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class MuteListTests
{
    [Fact]
    public void Nobody_is_muted_to_begin_with()
    {
        Assert.False(new MuteList().IsMuted(1));
    }

    [Fact]
    public void Muting_and_unmuting_round_trip()
    {
        var mutes = new MuteList();

        Assert.True(mutes.Mute(1));
        Assert.True(mutes.IsMuted(1));

        Assert.True(mutes.Unmute(1));
        Assert.False(mutes.IsMuted(1));
    }

    [Fact]
    public void Muting_twice_reports_no_change()
    {
        var mutes = new MuteList();
        mutes.Mute(1);

        Assert.False(mutes.Mute(1));
    }

    [Fact]
    public void Unmuting_someone_who_is_not_muted_reports_no_change()
    {
        Assert.False(new MuteList().Unmute(1));
    }

    [Fact]
    public void Mutes_are_keyed_by_steam_id_so_they_survive_a_reconnect()
    {
        var mutes = new MuteList();
        mutes.Mute(76561198000000000);

        Assert.True(mutes.IsMuted(76561198000000000));
        Assert.False(mutes.IsMuted(76561198000000001));
    }

    [Fact]
    public void The_muted_list_is_readable_for_the_panel()
    {
        var mutes = new MuteList();
        mutes.Mute(1);
        mutes.Mute(2);

        Assert.Equal(2, mutes.Muted.Count);
    }
}

public class WhitelistTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-wl-" + Guid.NewGuid());

    public WhitelistTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "whitelist.json");

    [Fact]
    public void With_the_mode_off_everyone_may_join()
    {
        var list = new Whitelist(FilePath) { Enabled = false };

        Assert.True(list.MayJoin(76561198000000000));
    }

    [Fact]
    public void With_the_mode_on_only_listed_players_may_join()
    {
        var list = new Whitelist(FilePath) { Enabled = true };
        list.Add(76561198000000000, "friend");

        Assert.True(list.MayJoin(76561198000000000));
        Assert.False(list.MayJoin(76561198000000001));
    }

    [Fact]
    public void Entries_round_trip_through_disk()
    {
        var list = new Whitelist(FilePath);
        list.Add(1, "someone");
        list.Save();

        var reloaded = new Whitelist(FilePath) { Enabled = true };
        reloaded.Load();

        Assert.True(reloaded.MayJoin(1));
        Assert.Equal("someone", reloaded.Entries[1]);
    }

    [Fact]
    public void Removing_takes_someone_off_the_list()
    {
        var list = new Whitelist(FilePath) { Enabled = true };
        list.Add(1, "someone");

        Assert.True(list.Remove(1));
        Assert.False(list.MayJoin(1));
        Assert.False(list.Remove(1));
    }

    [Fact]
    public void A_malformed_file_keeps_the_previous_list()
    {
        var list = new Whitelist(FilePath) { Enabled = true };
        list.Add(1, "someone");
        list.Save();
        list.Load();

        File.WriteAllText(FilePath, "{ not json");
        list.Load();

        Assert.True(list.MayJoin(1));
    }

    [Fact]
    public void An_empty_whitelist_with_the_mode_on_locks_everyone_out()
    {
        // Worth asserting rather than assuming: this is how an operator locks the
        // server, and it is also how they accidentally lock themselves out.
        var list = new Whitelist(FilePath) { Enabled = true };

        Assert.False(list.MayJoin(76561198000000000));
    }
}
