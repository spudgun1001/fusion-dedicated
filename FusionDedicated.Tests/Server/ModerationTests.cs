using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Banning from inside the game "did not always work" on a live server, and this
/// is why: it depends entirely on who is being banned.
/// </summary>
public class ModerationTests
{
    private static ModerationVerdict Ban(PermissionLevel sender, PermissionLevel target,
        PermissionLevel required = PermissionLevel.Owner)
        => Moderation.Check("ban", sender, target, required);

    [Fact]
    public void An_owner_can_ban_an_ordinary_player()
        => Assert.True(Ban(PermissionLevel.Owner, PermissionLevel.Default).Allowed);

    [Fact]
    public void An_owner_cannot_ban_another_owner()
    {
        // The rule that surprises people. A server with several owners has
        // banning that works on some players and not others, with nothing on
        // screen to say which.
        var verdict = Ban(PermissionLevel.Owner, PermissionLevel.Owner);

        Assert.False(verdict.Allowed);
        Assert.Contains("not below", verdict.Reason);
    }

    [Fact]
    public void An_operator_cannot_ban_when_banning_needs_an_owner()
    {
        var verdict = Ban(PermissionLevel.Operator, PermissionLevel.Default);

        Assert.False(verdict.Allowed);
        Assert.Contains("needs", verdict.Reason);
    }

    [Fact]
    public void An_operator_can_ban_when_banning_only_needs_an_operator()
        => Assert.True(Ban(PermissionLevel.Operator, PermissionLevel.Default,
            PermissionLevel.Operator).Allowed);

    [Fact]
    public void An_operator_cannot_ban_another_operator()
        => Assert.False(Ban(PermissionLevel.Operator, PermissionLevel.Operator,
            PermissionLevel.Operator).Allowed);

    [Fact]
    public void An_owner_can_ban_an_operator()
        => Assert.True(Ban(PermissionLevel.Owner, PermissionLevel.Operator).Allowed);

    [Fact]
    public void The_rank_is_checked_before_the_comparison()
    {
        // So the reason names the setting rather than the other player, which is
        // the thing an operator can actually change.
        var verdict = Ban(PermissionLevel.Default, PermissionLevel.Owner);

        Assert.Contains("needs", verdict.Reason);
        Assert.DoesNotContain("not below", verdict.Reason);
    }

    [Fact]
    public void A_refusal_always_says_why()
    {
        foreach (var sender in new[] { PermissionLevel.Guest, PermissionLevel.Default,
                     PermissionLevel.Operator, PermissionLevel.Owner })
        {
            foreach (var target in new[] { PermissionLevel.Guest, PermissionLevel.Default,
                         PermissionLevel.Operator, PermissionLevel.Owner })
            {
                var verdict = Ban(sender, target);

                if (!verdict.Allowed)
                {
                    Assert.False(string.IsNullOrWhiteSpace(verdict.Reason),
                        $"{sender} on {target} was refused with no reason");
                }
            }
        }
    }

    [Fact]
    public void Kicking_reads_the_same_way_with_its_own_setting()
    {
        Assert.True(Moderation.Check("kick", PermissionLevel.Operator,
            PermissionLevel.Default, PermissionLevel.Operator).Allowed);

        Assert.False(Moderation.Check("kick", PermissionLevel.Default,
            PermissionLevel.Default, PermissionLevel.Operator).Allowed);
    }
}
