using FusionDedicated.Server.Audit;

namespace FusionDedicated.Tests.Server;

public class AuditLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-audit-" + Guid.NewGuid());

    public AuditLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AuditLog Log() => new(_dir);

    [Fact]
    public void An_action_is_recorded_with_all_its_parts()
    {
        var log = Log();
        log.Record(AuditChannel.Console, "ban", "someone", 76561198000000000, "spawning nukes");

        var entry = Assert.Single(log.Recent(10));

        Assert.Equal("ban", entry.Action);
        Assert.Equal("someone", entry.Target);
        Assert.Equal(76561198000000000UL, entry.TargetId);
        Assert.Equal("spawning nukes", entry.Reason);
        Assert.Equal(AuditChannel.Console, entry.Channel);
    }

    [Fact]
    public void Entries_are_appended_to_a_file_that_survives_a_restart()
    {
        var log = Log();
        log.Record(AuditChannel.Panel, "kick", "someone", 1, "afk");

        var reopened = Log();

        Assert.Single(reopened.Recent(10));
    }

    [Fact]
    public void The_newest_entries_come_back_first()
    {
        var log = Log();
        log.Record(AuditChannel.Console, "kick", "first", 1, "");
        log.Record(AuditChannel.Console, "ban", "second", 2, "");

        Assert.Equal("second", log.Recent(10)[0].Target);
    }

    [Fact]
    public void Recent_honours_its_limit()
    {
        var log = Log();

        for (var i = 0; i < 20; i++)
        {
            log.Record(AuditChannel.Rcon, "kick", $"p{i}", (ulong)i, "");
        }

        Assert.Equal(5, log.Recent(5).Count);
    }

    [Fact]
    public void Every_channel_is_recorded_distinctly()
    {
        var log = Log();
        log.Record(AuditChannel.Console, "kick", "a", 1, "");
        log.Record(AuditChannel.Rcon, "kick", "b", 2, "");
        log.Record(AuditChannel.Panel, "kick", "c", 3, "");
        log.Record(AuditChannel.InGame, "kick", "d", 4, "");

        Assert.Equal(4, log.Recent(10).Select(e => e.Channel).Distinct().Count());
    }

    [Fact]
    public void A_corrupt_line_does_not_take_the_whole_log_down()
    {
        var log = Log();
        log.Record(AuditChannel.Console, "kick", "good", 1, "");

        File.AppendAllText(Path.Combine(_dir, AuditLog.FileName), "{ not json\n");

        Assert.Single(Log().Recent(10));
    }
}
