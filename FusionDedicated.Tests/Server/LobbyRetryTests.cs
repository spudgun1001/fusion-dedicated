using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The lobby was published once at startup and never again, so a Steam client
/// that signed in a minute later left the server invisible for the rest of its
/// life. Retrying costs nothing when it already worked.
/// </summary>
public class LobbyRetryTests
{
    [Fact]
    public void No_retry_while_the_lobby_is_published()
    {
        var due = new LobbyRetry(TimeSpan.FromSeconds(30));

        Assert.False(due.ShouldRetry(published: true, now: DateTime.UtcNow));
    }

    [Fact]
    public void First_retry_waits_for_the_interval()
    {
        var start = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        var due = new LobbyRetry(TimeSpan.FromSeconds(30), start);

        Assert.False(due.ShouldRetry(published: false, now: start.AddSeconds(29)));
        Assert.True(due.ShouldRetry(published: false, now: start.AddSeconds(30)));
    }

    [Fact]
    public void Asking_resets_the_clock()
    {
        var start = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        var due = new LobbyRetry(TimeSpan.FromSeconds(30), start);

        Assert.True(due.ShouldRetry(published: false, now: start.AddSeconds(30)));
        Assert.False(due.ShouldRetry(published: false, now: start.AddSeconds(45)));
        Assert.True(due.ShouldRetry(published: false, now: start.AddSeconds(60)));
    }

    [Fact]
    public void A_lobby_appearing_stops_the_retries()
    {
        var start = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        var due = new LobbyRetry(TimeSpan.FromSeconds(30), start);

        Assert.True(due.ShouldRetry(published: false, now: start.AddSeconds(30)));
        Assert.False(due.ShouldRetry(published: true, now: start.AddSeconds(120)));
    }

    [Fact]
    public void Losing_a_lobby_starts_them_again()
    {
        var start = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        var due = new LobbyRetry(TimeSpan.FromSeconds(30), start);

        Assert.False(due.ShouldRetry(published: true, now: start.AddSeconds(60)));
        Assert.True(due.ShouldRetry(published: false, now: start.AddSeconds(91)));
    }
}
