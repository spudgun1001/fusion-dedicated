using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class GlobalBanCheckTests
{
    private static GlobalBanList List() => new()
    {
        Bans =
        {
            new GlobalBanEntry
            {
                Username = "Daytrip",
                Reason = "Malicious Client Use",
                Platforms = { new GlobalBanPlatform { PlatformId = 76561198889496180, Platform = "Steam" } },
            },
        },
    };

    [Fact]
    public void Finds_a_listed_platform_id()
    {
        var found = GlobalBanCheck.Find(List(), 76561198889496180);

        Assert.NotNull(found);
        Assert.Equal("Malicious Client Use", found!.Reason);
    }

    [Fact]
    public void Returns_null_for_an_unlisted_id()
    {
        Assert.Null(GlobalBanCheck.Find(List(), 76561190000000000));
    }

    [Fact]
    public void Returns_null_for_a_null_list()
    {
        Assert.Null(GlobalBanCheck.Find(null, 76561198889496180));
    }

    [Fact]
    public void Handles_an_entry_with_no_platforms()
    {
        var list = new GlobalBanList { Bans = { new GlobalBanEntry { Username = "nobody" } } };

        Assert.Null(GlobalBanCheck.Find(list, 1));
    }
}
