using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class SafetyListParsingTests
{
    private const string ModBlacklistJson = """
    {
      "mods": [
        { "barcodes": [], "modID": 4423882, "nameID": "fursonas" },
        {
          "barcodes": [
            "SLZ.BONELAB.Core.Spawnable.RigManagerBlank",
            "SLZ.BONELAB.Core.Spawnable.GameplaySystems"
          ],
          "modID": -1,
          "nameID": "bonelab"
        }
      ]
    }
    """;

    private const string BanListJson = """
    {
      "bans": [
        {
          "username": "Daytrip",
          "reason": "Malicious Client Use",
          "games": [ { "game": "BONELAB" } ],
          "platforms": [ { "platformID": 76561198889496180, "platform": "Steam" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Parses_the_mod_blacklist()
    {
        var list = SafetyListParser.ParseModBlacklist(ModBlacklistJson);

        Assert.NotNull(list);
        Assert.Equal(2, list!.Mods.Count);
        Assert.Equal(4423882, list.Mods[0].ModId);
        Assert.Equal("fursonas", list.Mods[0].NameId);
        Assert.Contains("SLZ.BONELAB.Core.Spawnable.GameplaySystems", list.Mods[1].Barcodes);
    }

    [Fact]
    public void Parses_the_ban_list()
    {
        var list = SafetyListParser.ParseBanList(BanListJson);

        Assert.NotNull(list);
        var ban = Assert.Single(list!.Bans);
        Assert.Equal("Daytrip", ban.Username);
        Assert.Equal("Malicious Client Use", ban.Reason);
        Assert.Equal(76561198889496180UL, Assert.Single(ban.Platforms).PlatformId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"mods\": ")]
    public void Malformed_mod_blacklist_returns_null(string json)
    {
        Assert.Null(SafetyListParser.ParseModBlacklist(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public void Malformed_ban_list_returns_null(string json)
    {
        Assert.Null(SafetyListParser.ParseBanList(json));
    }

    [Fact]
    public void Missing_arrays_become_empty_rather_than_null()
    {
        var list = SafetyListParser.ParseModBlacklist("{}");

        Assert.NotNull(list);
        Assert.Empty(list!.Mods);
    }
}
