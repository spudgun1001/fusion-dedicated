using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>
/// The panel bound to every interface and then printed a placeholder, which told
/// nobody where to go. Pterodactyl hands the container its allocation, so use it.
/// </summary>
public class DashboardAddressTests
{
    [Fact]
    public void Allocated_address_is_used_when_known()
    {
        Assert.Equal(
            "http://65.109.102.54:8778/",
            DashboardAddress.Format("+", "65.109.102.54", 8778));
    }

    [Fact]
    public void Falls_back_to_a_placeholder_when_the_allocation_is_unknown()
    {
        Assert.Equal("http://<this-machine-ip>:8778/", DashboardAddress.Format("+", "", 8778));
        Assert.Equal("http://<this-machine-ip>:8778/", DashboardAddress.Format("*", null, 8778));
    }

    [Fact]
    public void A_specific_bind_host_is_shown_as_it_is()
    {
        Assert.Equal("http://127.0.0.1:8778/", DashboardAddress.Format("127.0.0.1", "", 8778));
    }

    [Fact]
    public void An_allocation_of_all_interfaces_is_not_a_usable_address()
    {
        Assert.Equal("http://<this-machine-ip>:8778/", DashboardAddress.Format("+", "0.0.0.0", 8778));
    }

    [Fact]
    public void Port_is_carried_through()
    {
        Assert.Equal("http://10.0.0.4:25580/", DashboardAddress.Format("+", "10.0.0.4", 25580));
    }
}
