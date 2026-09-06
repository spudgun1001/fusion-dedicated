namespace FusionDedicated.Web;

public enum PanelRole
{
    /// <summary>Sees everything, changes nothing.</summary>
    Viewer = 0,

    /// <summary>Deals with people and the world, but not the server itself.</summary>
    Moderator = 1,

    /// <summary>Everything, including settings, restarts and accounts.</summary>
    Owner = 2,
}
