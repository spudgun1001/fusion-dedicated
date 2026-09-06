using System.Text.Json.Serialization;
using FusionDedicated.Web;

namespace FusionDedicated.Plugins;

/// <summary>One choice in a field that offers a list.</summary>
public sealed class PluginOption
{
    public PluginOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    [JsonPropertyName("value")]
    public string Value { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; }
}

/// <summary>An input on a plugin page. A text box, or a list when it has options.</summary>
public sealed class PluginField
{
    public PluginField(string label, string key, string placeholder = "")
    {
        Label = label;
        Key = key;
        Placeholder = placeholder;
    }

    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; }

    [JsonPropertyName("placeholder")]
    public string Placeholder { get; set; }

    /// <summary>Choices to pick from. Empty leaves it a text box.</summary>
    [JsonPropertyName("options")]
    public List<PluginOption> Options { get; set; } = new();

    /// <summary>
    /// A list of the players connected right now, so an operator picks a name
    /// instead of copying a seventeen digit ID out of somewhere else.
    ///
    /// Pages are built on each request, so the list is whoever is on the server
    /// at the moment it is drawn. It is empty when nobody is connected, which is
    /// why the plugins that use this keep a text box beside it for someone who
    /// has already left.
    /// </summary>
    public static PluginField OfPlayers(
        string label, string key, IEnumerable<PluginPlayer> players, string blank = "Pick a player")
    {
        var field = new PluginField(label, key);

        field.Options.Add(new PluginOption("", blank));

        foreach (var player in players)
        {
            field.Options.Add(new PluginOption(
                player.PlatformId.ToString(),
                $"{player.Name} ({player.PlatformId})"));
        }

        return field;
    }
}

/// <summary>
/// A button. <see cref="Action"/> names the handler the plugin registered, and
/// <see cref="Arguments"/> is what that handler is given, which is how a row's
/// button knows which row it belongs to.
/// </summary>
public sealed class PluginButton
{
    public PluginButton(string label, string action, bool danger = false)
    {
        Label = label;
        Action = action;
        Danger = danger;
    }

    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; }

    /// <summary>Drawn as a destructive button, the way Remove and Ban are.</summary>
    [JsonPropertyName("danger")]
    public bool Danger { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, string> Arguments { get; set; } = new();
}

public sealed class PluginRow
{
    public PluginRow(IEnumerable<string> cells) => Cells = cells.ToList();

    [JsonPropertyName("cells")]
    public List<string> Cells { get; set; }

    [JsonPropertyName("actions")]
    public List<PluginButton> Actions { get; set; } = new();

    public PluginRow With(PluginButton button)
    {
        Actions.Add(button);
        return this;
    }
}

/// <summary>
/// One block on a page. A single type with a kind rather than a hierarchy,
/// because this is serialised to JSON and read by hand-written JavaScript, where
/// a subclass hierarchy would need a converter and buy nothing.
/// </summary>
public sealed class PluginSection
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = new();

    [JsonPropertyName("rows")]
    public List<PluginRow> Rows { get; set; } = new();

    [JsonPropertyName("fields")]
    public List<PluginField> Fields { get; set; } = new();

    [JsonPropertyName("buttons")]
    public List<PluginButton> Buttons { get; set; } = new();

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>
/// What a plugin wants shown. Only the shapes the panel already draws, so a
/// plugin page cannot drift from the rest of the panel and its author writes no
/// HTML.
/// </summary>
public sealed class PluginPage
{
    public PluginPage(string title) => Title = title;

    [JsonPropertyName("title")]
    public string Title { get; set; }

    /// <summary>The lowest panel role that may see and use this page.</summary>
    [JsonPropertyName("required")]
    public PanelRole Required { get; set; } = PanelRole.Moderator;

    [JsonPropertyName("sections")]
    public List<PluginSection> Sections { get; set; } = new();

    public PluginPage Table(string title, IEnumerable<string> columns, IEnumerable<PluginRow> rows)
    {
        Sections.Add(new PluginSection
        {
            Kind = "table",
            Title = title,
            Columns = columns.ToList(),
            Rows = rows.ToList(),
        });

        return this;
    }

    public PluginPage Fields(string title, IEnumerable<PluginField> fields,
        IEnumerable<PluginButton> buttons)
    {
        Sections.Add(new PluginSection
        {
            Kind = "fields",
            Title = title,
            Fields = fields.ToList(),
            Buttons = buttons.ToList(),
        });

        return this;
    }

    public PluginPage Note(string title, string text)
    {
        Sections.Add(new PluginSection { Kind = "note", Title = title, Text = text });
        return this;
    }
}
