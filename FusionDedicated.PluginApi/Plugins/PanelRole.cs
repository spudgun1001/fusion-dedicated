namespace FusionDedicated.Web;

public enum PanelRole
{
    /// <summary>Sees everything, changes nothing.</summary>
    Viewer = 0,

    /// <summary>Deals with people and the world, but not the server itself.</summary>
    Moderator = 1,

    /// <summary>Everything, including settings, restarts and accounts.</summary>
    Owner = 2,

    /// <summary>
    /// Moves money and nothing else.
    ///
    /// Deliberately off the end of the ladder rather than a rung on it. The other
    /// three are ordered, so more privilege is a higher number and a check is a
    /// comparison. A banker is not more or less than a moderator, they are
    /// somewhere else entirely, so every check names them rather than comparing
    /// them. Numbered last so the values the other three already had do not move
    /// under a plugin compiled against them.
    /// </summary>
    Banker = 3,
}
