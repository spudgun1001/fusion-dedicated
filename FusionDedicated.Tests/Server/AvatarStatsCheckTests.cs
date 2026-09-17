using System.Buffers.Binary;
using BonelabServerBrowser.Fusion;
using FusionDedicated;
using FusionDedicated.Server.Safety;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Server;

/// <summary>A modded client sent an avatar weighing 100,000,000 and the physics flung whoever it was sent to.</summary>
public class AvatarStatsCheckTests
{
    private static readonly ServerConfig Limits = new();

    private static byte[] With(string field, float value)
    {
        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, field, value);

        return stats;
    }

    [Fact]
    public void The_defaults_are_a_1000_avatar_500_a_part_scale_from_005_to_10_and_3_strikes_a_minute()
    {
        var config = new ServerConfig();

        Assert.Equal(1000f, config.MaxAvatarMass);
        Assert.Equal(500f, config.MaxAvatarPartMass);
        Assert.Equal(0.05f, config.MinAvatarScale);
        Assert.Equal(10f, config.MaxAvatarScale);
        Assert.Equal(3, config.AvatarStrikesBeforeKick);
        Assert.Equal(60, config.AvatarStrikeWindowSeconds);
    }

    [Fact]
    public void The_field_names_follow_SerializedAvatarStats()
    {
        var names = AvatarStatsCheck.FieldNames;

        Assert.Equal(FusionProtocol.AvatarStatFloatCount, names.Count);
        Assert.Equal(new[] { "localScale.x", "localScale.y", "localScale.z" }, names.Take(3));
        Assert.Equal(new[] { "massArm", "massChest", "massHead", "massLeg", "massPelvis", "massTotal" }, names.TakeLast(6));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Mass_total_is_the_last_float_written_big_endian()
    {
        var stats = ClientMessages.AvatarStats();
        BinaryPrimitives.WriteSingleBigEndian(stats.AsSpan(416, 4), 100000000f);

        Assert.StartsWith("massTotal ", AvatarStatsCheck.Problem(stats, Limits));
    }

    [Fact]
    public void A_normal_avatar_passes()
        => Assert.Null(AvatarStatsCheck.Problem(ClientMessages.AvatarStats(), Limits));

    [Theory]
    [InlineData("massArm")]
    [InlineData("massChest")]
    [InlineData("massHead")]
    [InlineData("massLeg")]
    [InlineData("massPelvis")]
    [InlineData("massTotal")]
    public void A_mass_of_a_hundred_million_fails_and_is_named(string field)
    {
        string? problem = AvatarStatsCheck.Problem(With(field, 100000000f), Limits);

        Assert.NotNull(problem);
        Assert.StartsWith($"{field} 100000000", problem);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void A_float_that_is_not_a_number_fails_wherever_it_is(float value)
    {
        Assert.StartsWith("headTop ", AvatarStatsCheck.Problem(With("headTop", value), Limits));
        Assert.StartsWith("intelligence ", AvatarStatsCheck.Problem(With("intelligence", value), Limits));
        Assert.StartsWith("massLeg ", AvatarStatsCheck.Problem(With("massLeg", value), Limits));
    }

    [Fact]
    public void A_negative_part_mass_fails()
        => Assert.StartsWith("massHead -1", AvatarStatsCheck.Problem(With("massHead", -1f), Limits));

    [Theory]
    [InlineData(0f)]
    [InlineData(-80f)]
    public void A_total_mass_of_zero_or_less_fails(float total)
        => Assert.StartsWith("massTotal ", AvatarStatsCheck.Problem(With("massTotal", total), Limits));

    [Theory]
    [InlineData("localScale.x", 0.01f)]
    [InlineData("localScale.y", 11f)]
    [InlineData("localScale.z", -1f)]
    public void A_scale_out_of_range_fails(string field, float value)
        => Assert.StartsWith($"{field} ", AvatarStatsCheck.Problem(With(field, value), Limits));

    [Fact]
    public void Values_on_the_limits_pass()
    {
        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "massArm", 500f);
        ClientMessages.SetAvatarStat(stats, "massPelvis", 0f);
        ClientMessages.SetAvatarStat(stats, "massTotal", 1000f);
        ClientMessages.SetAvatarStat(stats, "localScale.x", 0.05f);
        ClientMessages.SetAvatarStat(stats, "localScale.y", 10f);

        Assert.Null(AvatarStatsCheck.Problem(stats, Limits));
    }

    [Fact]
    public void A_block_of_the_wrong_size_fails()
        => Assert.NotNull(AvatarStatsCheck.Problem(new byte[100], Limits));

    [Fact]
    public void The_limits_come_from_the_settings()
    {
        var config = new ServerConfig { MaxAvatarPartMass = 2000f, MaxAvatarMass = 5000f };

        Assert.Null(AvatarStatsCheck.Problem(With("massChest", 1500f), config));
    }
}
