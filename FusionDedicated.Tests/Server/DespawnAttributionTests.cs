using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A despawn names the player who did it. Purging somebody attributed theirs to
/// them, and their own client was the only one that did not act on it, so their
/// props cleared for everybody else and stayed for them.
/// </summary>
public class DespawnAttributionTests
{
    [Fact]
    public void The_despawn_is_credited_to_somebody_other_than_the_recipient()
    {
        byte chosen = DespawnAttribution.For(recipient: 3, present: new byte[] { 1, 3, 5 });

        Assert.NotEqual(3, chosen);
        Assert.Contains(chosen, new byte[] { 1, 5 });
    }

    [Fact]
    public void The_first_other_player_is_taken_so_the_choice_is_stable()
    {
        Assert.Equal(1, DespawnAttribution.For(recipient: 3, present: new byte[] { 1, 3, 5 }));
    }

    [Fact]
    public void A_lone_player_can_only_be_credited_to_themselves()
    {
        // Nothing else is resolvable, and a despawn from an unknown sender is
        // ignored outright, so this is the best available rather than correct.
        Assert.Equal(4, DespawnAttribution.For(recipient: 4, present: new byte[] { 4 }));
    }

    [Fact]
    public void An_empty_server_falls_back_to_the_recipient()
    {
        Assert.Equal(2, DespawnAttribution.For(recipient: 2, present: Array.Empty<byte>()));
    }

    [Fact]
    public void Every_player_gets_an_attribution_that_is_not_themselves()
    {
        byte[] present = { 1, 2, 3, 4 };

        foreach (byte recipient in present)
        {
            Assert.NotEqual(recipient, DespawnAttribution.For(recipient, present));
        }
    }
}
