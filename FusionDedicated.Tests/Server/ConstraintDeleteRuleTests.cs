using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// What the server does with a client's constraint delete. A client only drops its
/// constraint when the delete comes back, so a refused one keeps it stuck for them.
/// </summary>
public class ConstraintDeleteRuleTests
{
    private static TrackedEntity End(byte owner)
    {
        var end = new EntityRegistry().Register(300, "fusion.constraint", owner, 0, 0, 0);
        end.Synthetic = true;

        return end;
    }

    [Fact]
    public void An_end_the_server_has_no_record_of_is_passed_on()
        => Assert.Equal(ConstraintDeleteVerdict.PassOn,
            ConstraintDeleteRule.Decide(null, 3, default, protectedServer: true));

    [Fact]
    public void A_prop_is_not_cleared()
    {
        var crate = new EntityRegistry().Register(400, "Pack.Spawnable.Crate", 3, 0, 0, 0);

        Assert.Equal(ConstraintDeleteVerdict.NotAConstraint,
            ConstraintDeleteRule.Decide(crate, 3, default, protectedServer: true));
    }

    [Fact]
    public void The_owner_clears_their_own_end()
        => Assert.Equal(ConstraintDeleteVerdict.Clear,
            ConstraintDeleteRule.Decide(End(3), 3, default, protectedServer: true));

    [Fact]
    public void Somebody_else_cannot_clear_it_on_a_protected_server()
        => Assert.Equal(ConstraintDeleteVerdict.NotYours,
            ConstraintDeleteRule.Decide(End(3), 4, default, protectedServer: true));

    [Fact]
    public void Somebody_else_can_clear_it_on_an_open_server()
        => Assert.Equal(ConstraintDeleteVerdict.Clear,
            ConstraintDeleteRule.Decide(End(3), 4, default, protectedServer: false));
}
