using System.Text.Json;
using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Plugins;

public class PluginPageTests
{
    [Fact]
    public void A_page_needs_a_moderator_unless_it_says_otherwise()
    {
        Assert.Equal(PanelRole.Moderator, new PluginPage("Police").Required);
    }

    [Fact]
    public void A_page_can_ask_for_an_owner()
    {
        Assert.Equal(PanelRole.Owner,
            new PluginPage("Police") { Required = PanelRole.Owner }.Required);
    }

    [Fact]
    public void A_table_keeps_its_columns_and_rows_in_order()
    {
        var page = new PluginPage("Police")
            .Table("Roster", new[] { "Name", "SteamID" }, new[]
            {
                new PluginRow(new[] { "Badger", "7656119800000001" }),
                new PluginRow(new[] { "Kanza", "7656119800000002" }),
            });

        var table = page.Sections.Single();

        Assert.Equal("table", table.Kind);
        Assert.Equal(new[] { "Name", "SteamID" }, table.Columns);
        Assert.Equal("Badger", table.Rows[0].Cells[0]);
        Assert.Equal("Kanza", table.Rows[1].Cells[0]);
    }

    [Fact]
    public void A_row_can_carry_its_own_buttons()
    {
        var row = new PluginRow(new[] { "Badger" })
            .With(new PluginButton("Remove", "remove", danger: true));

        Assert.Single(row.Actions);
        Assert.True(row.Actions[0].Danger);
        Assert.Equal("remove", row.Actions[0].Action);
    }

    [Fact]
    public void A_button_carries_the_arguments_its_row_needs()
    {
        var button = new PluginButton("Remove", "remove");
        button.Arguments["steamId"] = "7656119800000001";

        Assert.Equal("7656119800000001", button.Arguments["steamId"]);
    }

    [Fact]
    public void Fields_and_buttons_sit_in_one_section()
    {
        var page = new PluginPage("Police")
            .Fields("Add",
                new[] { new PluginField("SteamID", "steamId", "76561198000000000") },
                new[] { new PluginButton("Add", "add") });

        var section = page.Sections.Single();

        Assert.Equal("fields", section.Kind);
        Assert.Equal("steamId", section.Fields[0].Key);
        Assert.Equal("add", section.Buttons[0].Action);
    }

    [Fact]
    public void Sections_keep_the_order_they_were_added()
    {
        var page = new PluginPage("Police")
            .Note("First", "hello")
            .Table("Second", new[] { "a" }, Array.Empty<PluginRow>());

        Assert.Equal(new[] { "First", "Second" }, page.Sections.Select(s => s.Title));
    }

    [Fact]
    public void A_page_serialises_to_the_shape_the_panel_reads()
    {
        var page = new PluginPage("Police")
            .Table("Roster", new[] { "Name" }, new[] { new PluginRow(new[] { "Badger" }) });

        string json = JsonSerializer.Serialize(page);

        Assert.Contains("\"title\"", json);
        Assert.Contains("\"sections\"", json);
        Assert.Contains("\"kind\"", json);
        Assert.Contains("Badger", json);
    }
}
