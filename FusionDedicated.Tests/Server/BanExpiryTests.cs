using FusionDedicated.Server.Bans;

namespace FusionDedicated.Tests.Server;

public class BanDurationTests
{
    [Theory]
    [InlineData("30m", 30)]
    [InlineData("2h", 120)]
    [InlineData("7d", 10080)]
    [InlineData("1w", 10080)]
    [InlineData("45", 45)]
    [InlineData("90M", 90)]
    public void Durations_parse_to_minutes(string text, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), BanDuration.TryParse(text));
    }

    [Theory]
    [InlineData("permanent")]
    [InlineData("perm")]
    [InlineData("forever")]
    [InlineData("0")]
    public void Permanent_means_no_expiry(string text)
    {
        Assert.Null(BanDuration.TryParse(text));
        Assert.True(BanDuration.IsRecognised(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-5m")]
    [InlineData("5x")]
    public void Nonsense_is_not_recognised(string text)
    {
        Assert.False(BanDuration.IsRecognised(text));
    }
}

public class BanExpiryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-exp-" + Guid.NewGuid());

    public BanExpiryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private BanStore Store() => new(Path.Combine(_dir, "bans.json"));

    [Fact]
    public void A_ban_with_no_expiry_stays_permanent()
    {
        var store = Store();
        store.Ban(1, "x", "reason");

        Assert.True(store.IsBanned(1));
        Assert.Null(store.Find(1)!.ExpiresAt);
    }

    [Fact]
    public void An_unexpired_ban_still_applies()
    {
        var store = Store();
        store.Ban(1, "x", "reason", TimeSpan.FromHours(1));

        Assert.True(store.IsBanned(1));
    }

    [Fact]
    public void An_expired_ban_no_longer_applies()
    {
        var store = Store();
        store.Ban(1, "x", "reason", TimeSpan.FromMinutes(-1));

        Assert.False(store.IsBanned(1));
        Assert.Null(store.Find(1));
    }

    [Fact]
    public void Expired_bans_are_swept_from_the_list()
    {
        var store = Store();
        store.Ban(1, "gone", "reason", TimeSpan.FromMinutes(-1));
        store.Ban(2, "stays", "reason");

        int swept = store.SweepExpired();

        Assert.Equal(1, swept);
        Assert.Single(store.Entries);
        Assert.True(store.IsBanned(2));
    }

    [Fact]
    public void An_expiry_survives_a_round_trip()
    {
        var store = Store();
        store.Ban(1, "x", "reason", TimeSpan.FromHours(2));
        store.Save();

        var reloaded = Store();
        reloaded.Load();

        Assert.NotNull(reloaded.Find(1)!.ExpiresAt);
        Assert.True(reloaded.IsBanned(1));
    }
}
