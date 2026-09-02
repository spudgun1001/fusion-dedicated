namespace FusionDedicated.Web;

/// <summary>
/// Works out the address to print for the control panel. The listener binds every
/// interface, which is not something a person can type, so prefer the allocation
/// the host gave us.
/// </summary>
public static class DashboardAddress
{
    public static string Format(string bindHost, string? allocatedHost, int port)
    {
        if (bindHost is not ("+" or "*"))
        {
            return $"http://{bindHost}:{port}/";
        }

        if (!string.IsNullOrWhiteSpace(allocatedHost) && allocatedHost != "0.0.0.0")
        {
            return $"http://{allocatedHost}:{port}/";
        }

        return $"http://<this-machine-ip>:{port}/";
    }
}
