using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginBusTests
{
    private readonly PluginHealth _health = new();

    private PluginBus Bus() => new(_health, (_, _) => { });

    [Fact]
    public void A_plugin_disabled_by_its_failures_stops_answering()
    {
        var bus = Bus();
        bool fail = true;
        bus.Offer("labrp", "balance", _ => fail ? throw new InvalidOperationException("bad") : BusReply.Yes("5"));

        for (int i = 0; i < PluginHealth.FailuresBeforeDisable; i++)
        {
            var reply = bus.Ask("stocks", "labrp", "balance");
            Assert.True(reply.Offered);
            Assert.False(reply.Ok);
        }

        fail = false;

        Assert.False(bus.Ask("stocks", "labrp", "balance").Offered);
    }

    [Fact]
    public void A_disabled_plugin_offers_nothing()
    {
        var bus = Bus();
        bus.Offer("labrp", "charge", _ => BusReply.Yes());

        Assert.True(bus.Offers("labrp", "charge"));

        for (int i = 0; i < PluginHealth.FailuresBeforeDisable; i++) _health.NoteFailure("labrp");

        Assert.False(bus.Offers("labrp", "charge"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Asking_no_plugin_is_answered_rather_than_thrown(string? plugin)
        => Assert.False(Bus().Ask("stocks", plugin!, "balance").Offered);

    [Fact]
    public void A_healthy_plugin_answers()
    {
        var bus = Bus();
        bus.Offer("labrp", "balance", _ => BusReply.Yes("5"));

        Assert.Equal("5", bus.Ask("stocks", "labrp", "balance").Value);
    }
}
