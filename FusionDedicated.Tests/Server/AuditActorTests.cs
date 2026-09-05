using FusionDedicated.Server.Audit;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// With several accounts able to moderate, "the panel did it" stops being a
/// useful answer. The entry has to name the person.
/// </summary>
public class AuditActorTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-audit-" + Guid.NewGuid().ToString("N"));

    public AuditActorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void An_entry_records_who_did_it()
    {
        var log = new AuditLog(_dir);
        log.Record(AuditChannel.Panel, "ban", "Griefer", 76561198000000001, "nuke", "badger");

        Assert.Equal("badger", log.Recent(1).Single().Actor);
    }

    [Fact]
    public void An_action_with_no_named_actor_still_records()
    {
        var log = new AuditLog(_dir);
        log.Record(AuditChannel.Console, "mute", "Someone", 76561198000000002, "");

        var entry = log.Recent(1).Single();

        Assert.Equal("", entry.Actor);
        Assert.Equal(AuditChannel.Console, entry.Channel);
    }

    [Fact]
    public void The_actor_survives_being_written_and_read_back()
    {
        new AuditLog(_dir).Record(
            AuditChannel.Panel, "kick", "Crablet", 76561198000000003, "afk", "voidwalker");

        var reopened = new AuditLog(_dir);

        Assert.Equal("voidwalker", reopened.Recent(1).Single().Actor);
    }
}
