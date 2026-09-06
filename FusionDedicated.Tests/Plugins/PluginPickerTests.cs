using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginFieldOptionsTests
{
    private static readonly PluginPlayer[] Two =
    {
        new(76561198000000001, 3, "Terminator", PermissionLevel.Owner),
        new(76561198000000002, 4, "Kanzaaa", PermissionLevel.Operator),
    };

    [Fact]
    public void A_plain_field_offers_nothing_to_pick_from()
        => Assert.Empty(new PluginField("SteamID", "steamId").Options);

    [Fact]
    public void A_player_field_lists_everyone_connected()
    {
        var field = PluginField.OfPlayers("Player", "player", Two);

        // The blank one first, so nothing is picked by accident.
        Assert.Equal(3, field.Options.Count);
        Assert.Equal("", field.Options[0].Value);
        Assert.Equal("76561198000000001", field.Options[1].Value);
        Assert.Equal("76561198000000002", field.Options[2].Value);
    }

    [Fact]
    public void A_player_is_shown_by_name_with_the_id_beside_it()
    {
        // The ID is there because two people can share a name, and an operator
        // needs to be sure which one they picked.
        var field = PluginField.OfPlayers("Player", "player", Two);

        Assert.Equal("Terminator (76561198000000001)", field.Options[1].Label);
    }

    [Fact]
    public void An_empty_server_still_gives_a_usable_field()
    {
        var field = PluginField.OfPlayers("Player", "player", Array.Empty<PluginPlayer>());

        Assert.Single(field.Options);
        Assert.Equal("Pick a player", field.Options[0].Label);
    }
}

public class PluginValuesTests
{
    private static Dictionary<string, string> Values(string picked, string typed)
        => new() { ["player"] = picked, ["steamId"] = typed };

    [Fact]
    public void The_picked_player_is_used()
        => Assert.Equal(76561198000000001UL,
            PluginValues.PlayerId(Values("76561198000000001", "")));

    [Fact]
    public void A_typed_id_is_used_when_nobody_was_picked()
        => Assert.Equal(76561198000000002UL,
            PluginValues.PlayerId(Values("", "76561198000000002")));

    [Fact]
    public void Picking_somebody_beats_whatever_is_in_the_box()
    {
        // The box keeps its text between visits, so a stale ID may be sitting in
        // it. Choosing a name is the deliberate act.
        Assert.Equal(76561198000000001UL,
            PluginValues.PlayerId(Values("76561198000000001", "76561198000000009")));
    }

    [Fact]
    public void Neither_filled_in_is_nobody()
        => Assert.Null(PluginValues.PlayerId(Values("", "")));

    [Fact]
    public void Surrounding_space_is_ignored()
        => Assert.Equal(76561198000000001UL,
            PluginValues.PlayerId(Values("", "  76561198000000001  ")));

    [Theory]
    [InlineData("not an id")]
    [InlineData("-1")]
    [InlineData("0")]
    public void Something_that_is_not_an_id_is_nobody(string typed)
        => Assert.Null(PluginValues.PlayerId(Values("", typed)));

    [Fact]
    public void A_missing_key_is_nobody_rather_than_a_throw()
        => Assert.Null(PluginValues.PlayerId(new Dictionary<string, string>()));

    [Fact]
    public void The_keys_can_be_named_by_the_caller()
        => Assert.Equal(7UL, PluginValues.PlayerId(
            new Dictionary<string, string> { ["who"] = "7" }, "who", "orTyped"));
}
