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
    public void The_defaults_are_a_5000_avatar_2500_a_part_scale_up_to_50_and_3_strikes_a_minute()
    {
        var config = new ServerConfig();

        Assert.Equal(5000f, config.MaxAvatarMass);
        Assert.Equal(2500f, config.MaxAvatarPartMass);
        Assert.Equal(50f, config.MaxAvatarScale);
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
    [InlineData("localScale.x", 60f)]
    [InlineData("localScale.y", 60f)]
    [InlineData("localScale.z", -60f)]
    public void A_scale_over_the_ceiling_fails(string field, float value)
        => Assert.StartsWith($"{field} ", AvatarStatsCheck.Problem(With(field, value), Limits));

    [Fact]
    public void Values_on_the_limits_pass()
    {
        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "massArm", 2500f);
        ClientMessages.SetAvatarStat(stats, "massPelvis", 0f);
        ClientMessages.SetAvatarStat(stats, "massTotal", 5000f);
        ClientMessages.SetAvatarStat(stats, "localScale.x", 0.005f);
        ClientMessages.SetAvatarStat(stats, "localScale.y", 50f);

        Assert.Null(AvatarStatsCheck.Problem(stats, Limits));
    }

    [Fact]
    public void A_block_of_the_wrong_size_fails()
        => Assert.NotNull(AvatarStatsCheck.Problem(new byte[100], Limits));

    [Fact]
    public void The_limits_come_from_the_settings()
    {
        var config = new ServerConfig { MaxAvatarPartMass = 6000f, MaxAvatarMass = 9000f };

        Assert.Null(AvatarStatsCheck.Problem(With("massChest", 5500f), config));
    }

    [Theory]
    [InlineData("massArm", 100000000f)]
    [InlineData("massTotal", 100000000f)]
    [InlineData("massHead", -1f)]
    [InlineData("massTotal", 0f)]
    [InlineData("massTotal", -80f)]
    [InlineData("localScale.z", 200000f)]
    [InlineData("speed", 2000000000f)]
    [InlineData("headTop", -2000000000f)]
    [InlineData("chinY", float.Epsilon)]
    [InlineData("massTotal", float.Epsilon)]
    [InlineData("height", float.NaN)]
    public void Values_no_real_avatar_can_have_are_impossible(string field, float value)
    {
        var verdict = AvatarStatsCheck.Check(With(field, value), Limits);

        Assert.StartsWith($"{field} ", verdict.Problem);
        Assert.True(verdict.Impossible);
    }

    [Theory]
    [InlineData("massTotal", 6000f)]
    [InlineData("massTotal", 0.5f)]
    [InlineData("massLeg", 3000f)]
    [InlineData("localScale.x", 60f)]
    [InlineData("height", 100f)]
    [InlineData("armLength", -0.5f)]
    [InlineData("headEllipseX", -0.1f)]
    [InlineData("kneeEllipse.XRadius", -0.1f)]
    public void Values_merely_over_the_limits_fail_but_are_not_impossible(string field, float value)
    {
        var verdict = AvatarStatsCheck.Check(With(field, value), Limits);

        Assert.StartsWith($"{field} ", verdict.Problem);
        Assert.False(verdict.Impossible);
    }

    [Theory]
    [InlineData("height", 0.0088f)]
    [InlineData("height", 0.0001f)]
    [InlineData("height", 88f)]
    [InlineData("localScale.y", 0.001f)]
    [InlineData("kneeEllipse.XBias", -0.5f)]
    [InlineData("speed", 500000f)]
    [InlineData("headTop", -500000f)]
    [InlineData("massTotal", 1f)]
    public void Values_inside_the_limits_pass(string field, float value)
        => Assert.Null(AvatarStatsCheck.Problem(With(field, value), Limits));

    /// <summary>Values real players were dropped or refused for on 2026-09-18, before the limits were widened.</summary>
    [Theory]
    [InlineData("localScale.x", 0.01f)]
    [InlineData("localScale.x", 0.01905f)]
    [InlineData("localScale.x", 0.001f)]
    [InlineData("localScale.x", 0.022f)]
    [InlineData("localScale.x", 40f)]
    [InlineData("massTotal", 1649.314f)]
    [InlineData("strengthUpper", 363583.9f)]
    [InlineData("vitality", 100000.1f)]
    public void Avatars_real_players_wear_pass(string field, float value)
        => Assert.Null(AvatarStatsCheck.Problem(With(field, value), Limits));

    [Fact]
    public void Height_follows_the_scale_ceiling()
    {
        var config = new ServerConfig { MaxAvatarScale = 2f };

        Assert.StartsWith("height ", AvatarStatsCheck.Problem(With("height", 3.6f), config));
        Assert.Null(AvatarStatsCheck.Problem(With("height", 3.5f), config));
        Assert.Null(AvatarStatsCheck.Problem(With("height", 1.7f), config));
    }

    [Fact]
    public void A_limit_of_zero_or_less_turns_that_bound_off()
    {
        var config = new ServerConfig { MaxAvatarMass = 0f, MaxAvatarPartMass = -1f, MaxAvatarScale = 0f };

        Assert.Null(AvatarStatsCheck.Problem(With("massTotal", 5000f), config));
        Assert.Null(AvatarStatsCheck.Problem(With("massChest", 5000f), config));
        Assert.Null(AvatarStatsCheck.Problem(With("localScale.x", 0.0001f), config));
        Assert.Null(AvatarStatsCheck.Problem(With("localScale.y", 90f), config));
        Assert.Null(AvatarStatsCheck.Problem(With("height", 0.0001f), config));
        Assert.Null(AvatarStatsCheck.Problem(With("height", 200f), config));
        Assert.True(AvatarStatsCheck.Check(With("massTotal", 100000000f), config).Impossible);
    }

    /// <summary>Ported avatars are often mirrored on one axis, and squashing that axis flattened them for everyone else.</summary>
    [Fact]
    public void A_mirrored_axis_keeps_its_sign_and_only_its_size_is_limited()
    {
        Assert.Null(AvatarStatsCheck.Problem(With("localScale.x", -1f), Limits));
        Assert.Equal("localScale.x -60, over 50", AvatarStatsCheck.Problem(With("localScale.x", -60f), Limits));
        Assert.Null(AvatarStatsCheck.Problem(With("localScale.z", -0.001f), Limits));

        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "localScale.x", -60f);
        ClientMessages.SetAvatarStat(stats, "localScale.y", -1f);
        ClientMessages.SetAvatarStat(stats, "localScale.z", -0.001f);

        byte[] clamped = AvatarStatsCheck.Clamp(stats, Limits);

        Assert.Null(AvatarStatsCheck.Problem(clamped, Limits));
        Assert.Equal(-50f, Stat(clamped, "localScale.x"));
        Assert.Equal(-1f, Stat(clamped, "localScale.y"));
        Assert.Equal(-0.001f, Stat(clamped, "localScale.z"));
    }

    /// <summary>Only the three scale floats mirror. A negative length is still pulled up to zero.</summary>
    [Fact]
    public void A_negative_length_is_not_treated_as_a_mirror()
    {
        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "armLength", -1f);
        ClientMessages.SetAvatarStat(stats, "height", -1.76f);

        byte[] clamped = AvatarStatsCheck.Clamp(stats, Limits);

        Assert.Equal(0f, Stat(clamped, "armLength"));
        Assert.Equal(0f, Stat(clamped, "height"));
    }

    [Fact]
    public void A_maximum_under_its_own_minimum_is_no_limit_at_all()
        => Assert.Null(AvatarStatsCheck.Problem(With("massTotal", 80f), new ServerConfig { MaxAvatarMass = 0.5f }));

    [Fact]
    public void Every_limit_the_bounds_ignore_is_named_at_the_start()
    {
        Assert.Empty(AvatarStatsCheck.SettingWarnings(new ServerConfig()));
        Assert.Empty(AvatarStatsCheck.SettingWarnings(new ServerConfig { MaxAvatarScale = 0f }));

        Assert.Equal(new[] { "MaxAvatarMass 0.5 is under the 1 every avatar weighs, so it is ignored until it is fixed" },
            AvatarStatsCheck.SettingWarnings(new ServerConfig { MaxAvatarMass = 0.5f }));

        Assert.Equal(new[] { "MaxAvatarScale Infinity is not a finite number, so fix it before trusting the avatar limits" },
            AvatarStatsCheck.SettingWarnings(new ServerConfig { MaxAvatarScale = float.PositiveInfinity }));
    }

    [Fact]
    public void Clamping_brings_every_limit_back_inside()
    {
        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "localScale.y", 60f);
        ClientMessages.SetAvatarStat(stats, "height", 100f);
        ClientMessages.SetAvatarStat(stats, "armLength", -1f);
        ClientMessages.SetAvatarStat(stats, "speed", 500000f);
        ClientMessages.SetAvatarStat(stats, "massLeg", 3000f);
        ClientMessages.SetAvatarStat(stats, "massTotal", 0.5f);

        byte[] clamped = AvatarStatsCheck.Clamp(stats, Limits);

        Assert.Null(AvatarStatsCheck.Problem(clamped, Limits));
        Assert.Equal(50f, Stat(clamped, "localScale.y"));
        Assert.Equal(88f, Stat(clamped, "height"), 3);
        Assert.Equal(0f, Stat(clamped, "armLength"));
        Assert.Equal(500000f, Stat(clamped, "speed"));
        Assert.Equal(2500f, Stat(clamped, "massLeg"));
        Assert.Equal(1f, Stat(clamped, "massTotal"));
        Assert.Equal(1f, Stat(clamped, "localScale.x"));
    }

    [Fact]
    public void The_scale_warning_is_given_when_the_server_starts()
        => Assert.Contains("AvatarStatsCheck.SettingWarnings(Config)", FusionServerSource.Method("public void Start("));

    private static float Stat(byte[] stats, string field)
        => BinaryPrimitives.ReadSingleBigEndian(stats.AsSpan(AvatarStatsCheck.FieldNames.ToList().IndexOf(field) * 4, 4));
}
