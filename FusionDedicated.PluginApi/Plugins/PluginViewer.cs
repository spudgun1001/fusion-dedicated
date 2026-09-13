using FusionDedicated.Web;

namespace FusionDedicated.Plugins;

/// <summary>The panel account a plugin page is being built for.</summary>
/// <param name="Name">The signed-in account, the same name a button handler reads with PluginValues.PanelUser.</param>
public readonly record struct PluginViewer(string Name, PanelRole Role);
