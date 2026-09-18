using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class MessageBudgetTests
{
    private readonly DateTime _t0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static MessageBudget Budget(int metadata = 3, int avatars = 2, int rpc = 5)
        => new(new ServerConfig
        {
            MetadataPerSecond = metadata,
            AvatarSwapsPerSecond = avatars,
            RpcMessagesPerSecond = rpc,
        });

    [Fact]
    public void The_defaults_are_ten_metadata_two_avatars_and_sixty_rpc_messages_a_second()
    {
        var config = new ServerConfig();

        Assert.Equal(10, config.MetadataPerSecond);
        Assert.Equal(2, config.AvatarSwapsPerSecond);
        Assert.Equal(250, config.RpcMessagesPerSecond);
    }

    [Fact]
    public void Messages_within_the_allowance_are_allowed()
    {
        var budget = Budget(metadata: 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(budget.Allow(1, MessageKind.Metadata, _t0));
        }
    }

    [Fact]
    public void The_message_over_the_allowance_is_refused()
    {
        var budget = Budget(metadata: 3);

        for (var i = 0; i < 3; i++)
        {
            budget.Allow(1, MessageKind.Metadata, _t0);
        }

        Assert.False(budget.Allow(1, MessageKind.Metadata, _t0.AddMilliseconds(500)));
    }

    [Fact]
    public void The_allowance_resets_each_second()
    {
        var budget = Budget(avatars: 2);

        budget.Allow(1, MessageKind.Avatar, _t0);
        budget.Allow(1, MessageKind.Avatar, _t0);

        Assert.False(budget.Allow(1, MessageKind.Avatar, _t0.AddMilliseconds(999)));
        Assert.True(budget.Allow(1, MessageKind.Avatar, _t0.AddSeconds(1)));
    }

    [Fact]
    public void Each_kind_has_its_own_allowance()
    {
        var budget = Budget(metadata: 1, avatars: 1, rpc: 1);

        Assert.True(budget.Allow(1, MessageKind.Avatar, _t0));
        Assert.False(budget.Allow(1, MessageKind.Avatar, _t0));
        Assert.True(budget.Allow(1, MessageKind.Metadata, _t0));
        Assert.True(budget.Allow(1, MessageKind.Rpc, _t0));
    }

    [Fact]
    public void Players_are_limited_independently()
    {
        var budget = Budget(rpc: 1);

        Assert.True(budget.Allow(1, MessageKind.Rpc, _t0));
        Assert.False(budget.Allow(1, MessageKind.Rpc, _t0));
        Assert.True(budget.Allow(2, MessageKind.Rpc, _t0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_limit_of_zero_or_less_allows_everything(int limit)
    {
        var budget = Budget(rpc: limit);

        for (var i = 0; i < 500; i++)
        {
            Assert.True(budget.Allow(1, MessageKind.Rpc, _t0));
        }

        Assert.Empty(budget.DueSummaries(_t0.AddMinutes(5)));
    }

    [Fact]
    public void A_changed_setting_applies_at_once()
    {
        var config = new ServerConfig { RpcMessagesPerSecond = 1 };
        var budget = new MessageBudget(config);

        Assert.True(budget.Allow(1, MessageKind.Rpc, _t0));
        Assert.False(budget.Allow(1, MessageKind.Rpc, _t0));

        config.RpcMessagesPerSecond = 0;

        Assert.True(budget.Allow(1, MessageKind.Rpc, _t0));
    }

    [Fact]
    public void No_summary_is_due_before_a_minute_has_passed()
    {
        var budget = Budget(metadata: 1);

        budget.Allow(1, MessageKind.Metadata, _t0);
        budget.Allow(1, MessageKind.Metadata, _t0);
        budget.Allow(1, MessageKind.Metadata, _t0);

        Assert.Empty(budget.DueSummaries(_t0.AddSeconds(59)));
    }

    [Fact]
    public void A_summary_a_minute_after_the_first_drop_counts_every_drop_since()
    {
        var budget = Budget(metadata: 1);

        budget.Allow(1, MessageKind.Metadata, _t0);
        budget.Allow(1, MessageKind.Metadata, _t0);
        budget.Allow(1, MessageKind.Metadata, _t0.AddSeconds(30));
        budget.Allow(1, MessageKind.Metadata, _t0.AddSeconds(30));

        var summary = Assert.Single(budget.DueSummaries(_t0.AddSeconds(60)));

        Assert.Equal((byte)1, summary.SmallId);
        Assert.Equal(MessageKind.Metadata, summary.Kind);
        Assert.Equal(2, summary.Dropped);
    }

    [Fact]
    public void A_summary_is_given_once_and_the_count_starts_again()
    {
        var budget = Budget(avatars: 1);

        budget.Allow(1, MessageKind.Avatar, _t0);
        budget.Allow(1, MessageKind.Avatar, _t0);

        Assert.Single(budget.DueSummaries(_t0.AddSeconds(60)));
        Assert.Empty(budget.DueSummaries(_t0.AddSeconds(61)));

        budget.Allow(1, MessageKind.Avatar, _t0.AddSeconds(70));
        budget.Allow(1, MessageKind.Avatar, _t0.AddSeconds(70));

        Assert.Empty(budget.DueSummaries(_t0.AddSeconds(120)));
        Assert.Equal(1, Assert.Single(budget.DueSummaries(_t0.AddSeconds(130))).Dropped);
    }

    [Fact]
    public void Nothing_is_summed_up_for_a_player_who_dropped_nothing()
    {
        var budget = Budget(metadata: 3);

        budget.Allow(1, MessageKind.Metadata, _t0);

        Assert.Empty(budget.DueSummaries(_t0.AddMinutes(2)));
    }

    [Fact]
    public void Forgetting_a_player_clears_their_allowance_and_their_drops()
    {
        var budget = Budget(metadata: 1);

        budget.Allow(1, MessageKind.Metadata, _t0);
        budget.Allow(1, MessageKind.Metadata, _t0);
        budget.Forget(1);

        Assert.True(budget.Allow(1, MessageKind.Metadata, _t0));
        Assert.Empty(budget.DueSummaries(_t0.AddSeconds(60)));
    }

    [Theory]
    [InlineData(MessageKind.Metadata, "metadata")]
    [InlineData(MessageKind.Avatar, "avatar")]
    [InlineData(MessageKind.Rpc, "RPC")]
    public void Each_kind_has_the_word_the_log_uses(MessageKind kind, string word)
        => Assert.Equal(word, MessageBudget.Word(kind));
}
