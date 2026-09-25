using FusionDedicated.Plugins;

namespace FusionDedicated.Web;

/// <summary>What of a plugin page one panel account is shown and may press.</summary>
public static class PluginPageAccess
{
    /// <summary>
    /// The page built for this account, with the blocks it may not see taken out,
    /// or null when nothing is left for it.
    ///
    /// Filtered here rather than when the page is built, so a plugin describes
    /// its page once and the panel decides who sees which part of it.
    /// </summary>
    public static PluginPage? Visible(PluginPanel? panel, string plugin, PluginViewer viewer)
    {
        var page = panel?.Build(plugin, viewer);

        if (page == null)
        {
            return null;
        }

        page.Sections = page.Sections
            .Where(section => PanelPermissions.CanSee(viewer.Role, section.Required))
            .ToList();

        if (page.Sections.Count == 0)
        {
            return null;
        }

        // A banker is allowed a page by the blocks marked for them, not by the
        // page's own floor, which every page sets to moderator.
        return viewer.Role == PanelRole.Banker || PanelPermissions.CanSee(viewer.Role, page.Required)
            ? page
            : null;
    }

    /// <summary>
    /// Whether an action is one of the buttons this account is shown. Row buttons
    /// and tree node buttons count as well as a block's own.
    /// </summary>
    public static bool MayInvoke(PluginPanel? panel, string plugin, string action, PluginViewer viewer)
        => Visible(panel, plugin, viewer) is { } page
           && page.Sections.Any(section =>
               section.Buttons.Any(b => b.Action == action)
               || section.Rows.Any(r => r.Actions.Any(b => b.Action == action))
               || section.Nodes.Any(n => HasButton(n, action)));

    private static bool HasButton(PluginTreeNode node, string action)
        => node.Buttons.Any(b => b.Action == action) || node.Children.Any(c => HasButton(c, action));
}
