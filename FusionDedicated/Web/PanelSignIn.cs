namespace FusionDedicated.Web;

/// <summary>
/// Works out who is asking and what they are allowed to be. The account from
/// the panel's own settings is always an owner, so a corrupt accounts file or a
/// forgotten password never shuts everyone out of a running server.
/// </summary>
public static class PanelSignIn
{
    public static PanelRole? Resolve(
        string? authorizationHeader,
        string configuredUser,
        string configuredPassword,
        PanelUsers users)
    {
        var parsed = DashboardAuth.TryParseBasic(authorizationHeader);

        // A named account still gets its own role when no panel password is set,
        // so turning one off does not silently promote everybody.
        if (parsed is { } credentials)
        {
            if (DashboardAuth.ConstantTimeEquals(credentials.User, configuredUser)
                && !string.IsNullOrEmpty(configuredPassword)
                && DashboardAuth.ConstantTimeEquals(credentials.Password, configuredPassword))
            {
                return PanelRole.Owner;
            }

            if (users.Authenticate(credentials.User, credentials.Password) is { } role)
            {
                return role;
            }
        }

        // No password configured is the existing behaviour for a loopback panel,
        // which Start already refuses to expose on any other interface.
        if (string.IsNullOrEmpty(configuredPassword))
        {
            return PanelRole.Owner;
        }

        return null;
    }
}
