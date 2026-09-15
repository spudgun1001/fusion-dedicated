using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>How often an owner asking again for what it already owns is answered.</summary>
public class OwnershipConfirmationsTests
{
    private static readonly DateTime Start = new(2026, 9, 15, 19, 14, 31, DateTimeKind.Utc);

    private DateTime _now = Start;

    private OwnershipConfirmations Confirmations() => new(() => _now);

    [Fact]
    public void The_first_repeat_is_confirmed()
        => Assert.True(Confirmations().ShouldConfirm(player: 2, entity: 2365));

    [Fact]
    public void Another_inside_half_a_second_is_not()
    {
        var confirmations = Confirmations();
        confirmations.ShouldConfirm(2, 2365);

        _now = Start.AddMilliseconds(499);

        Assert.False(confirmations.ShouldConfirm(2, 2365));
    }

    [Fact]
    public void Another_after_half_a_second_is()
    {
        var confirmations = Confirmations();
        confirmations.ShouldConfirm(2, 2365);

        _now = Start.AddMilliseconds(500);

        Assert.True(confirmations.ShouldConfirm(2, 2365));
    }

    [Fact]
    public void The_half_second_runs_from_the_last_confirmation_sent()
    {
        var confirmations = Confirmations();
        confirmations.ShouldConfirm(2, 2365);

        _now = Start.AddMilliseconds(400);
        confirmations.ShouldConfirm(2, 2365);

        _now = Start.AddMilliseconds(500);

        Assert.True(confirmations.ShouldConfirm(2, 2365));
    }

    [Fact]
    public void Each_player_and_entity_is_counted_on_its_own()
    {
        var confirmations = Confirmations();
        confirmations.ShouldConfirm(2, 2365);

        Assert.True(confirmations.ShouldConfirm(3, 2365));
        Assert.True(confirmations.ShouldConfirm(2, 2366));
        Assert.False(confirmations.ShouldConfirm(2, 2365));
    }

    [Fact]
    public void Forgetting_a_player_lets_their_next_repeat_through_and_leaves_others()
    {
        var confirmations = Confirmations();
        confirmations.ShouldConfirm(2, 2365);
        confirmations.ShouldConfirm(3, 2365);

        confirmations.ForgetPlayer(2);

        Assert.True(confirmations.ShouldConfirm(2, 2365));
        Assert.False(confirmations.ShouldConfirm(3, 2365));
    }

    [Fact]
    public void Forgetting_an_entity_lets_its_next_repeat_through_and_leaves_others()
    {
        var confirmations = Confirmations();
        confirmations.ShouldConfirm(2, 2365);
        confirmations.ShouldConfirm(2, 2366);

        confirmations.ForgetEntity(2365);

        Assert.True(confirmations.ShouldConfirm(2, 2365));
        Assert.False(confirmations.ShouldConfirm(2, 2366));
    }

    [Fact]
    public void Clearing_lets_every_next_repeat_through()
    {
        var confirmations = Confirmations();
        confirmations.ShouldConfirm(2, 2365);
        confirmations.ShouldConfirm(3, 2366);

        confirmations.Clear();

        Assert.True(confirmations.ShouldConfirm(2, 2365));
        Assert.True(confirmations.ShouldConfirm(3, 2366));
    }

    [Fact]
    public void A_player_who_leaves_is_forgotten()
        => Assert.Contains("_confirmations.ForgetPlayer(player.SmallId);", FusionServerSource.Method("private void Depart("));

    [Fact]
    public void A_removed_entity_is_forgotten()
        => Assert.Contains("Entities.Removed += id => _confirmations.ForgetEntity(id);", FusionServerSource.Text());

    [Fact]
    public void A_level_change_forgets_everything()
        => Assert.Contains("_confirmations.Clear();", FusionServerSource.Method("public void SetLevel("));
}
