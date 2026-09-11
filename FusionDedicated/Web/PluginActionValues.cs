using System.Collections.Specialized;
using FusionDedicated.Plugins;

namespace FusionDedicated.Web;

/// <summary>What a plugin action is handed: the form, and the account that sent it.</summary>
public static class PluginActionValues
{
    public static Dictionary<string, string> From(NameValueCollection query, string panelUser)
    {
        var values = query.AllKeys
            .Where(k => k != null && k != "plugin" && k != "action")
            .ToDictionary(k => k!, k => query[k] ?? "");

        // Set by the server over anything the browser sent, so nobody can sign a change as somebody else.
        values[PluginValues.PanelUserKey] = panelUser;

        return values;
    }
}
