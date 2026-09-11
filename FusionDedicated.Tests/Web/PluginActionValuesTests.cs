using System.Collections.Specialized;
using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>What a plugin action is handed: the form, and who sent it.</summary>
public class PluginActionValuesTests
{
    private static NameValueCollection Query(params (string Key, string Value)[] pairs)
    {
        var query = new NameValueCollection();

        foreach (var (key, value) in pairs)
        {
            query[key] = value;
        }

        return query;
    }

    [Fact]
    public void The_form_is_passed_on_without_the_plugin_and_action()
    {
        var values = PluginActionValues.From(
            Query(("plugin", "labrp"), ("action", "setSalary"), ("salary", "500")), "Sam");

        Assert.Equal("500", values["salary"]);
        Assert.False(values.ContainsKey("plugin"));
        Assert.False(values.ContainsKey("action"));
    }

    [Fact]
    public void The_signed_in_account_is_passed_on()
        => Assert.Equal("Sam",
            PluginActionValues.From(Query(("salary", "500")), "Sam")[PluginValues.PanelUserKey]);

    [Fact]
    public void A_browser_cannot_name_somebody_else()
        => Assert.Equal("Sam",
            PluginActionValues.From(Query((PluginValues.PanelUserKey, "admin")), "Sam")[PluginValues.PanelUserKey]);

    [Fact]
    public void The_dashboard_hands_plugin_actions_these_values()
        => Assert.Contains("PluginActionValues.From(query, ActorFor(context))", File.ReadAllText(DashboardPath()));

    private static string DashboardPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "FusionDedicated", "Web", "Dashboard.cs");

            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Dashboard.cs");
    }
}
