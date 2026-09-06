using FusionDedicated;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The rank list is a list, so a file written by hand or by an older build can
/// hold the same SteamID twice, sometimes under two different names. Only one of
/// them was ever in effect, and the panel showed them all.
/// </summary>
public class PermissionDedupeTests
{
    private static ServerConfig WithPermissions(params PermissionEntry[] entries)
        => new() { Permissions = entries.ToList() };

    [Fact]
    public void The_same_id_twice_becomes_one_entry()
    {
        var config = WithPermissions(
            new PermissionEntry { PlatformId = 1, Username = "Terminator", Level = PermissionLevel.Owner },
            new PermissionEntry { PlatformId = 1, Username = "Terminator", Level = PermissionLevel.Owner });

        Assert.Equal(1, config.DedupePermissions());
        Assert.Single(config.Permissions);
    }

    [Fact]
    public void The_highest_rank_wins_so_nobody_is_quietly_demoted()
    {
        var config = WithPermissions(
            new PermissionEntry { PlatformId = 1, Username = "Kanza", Level = PermissionLevel.Operator },
            new PermissionEntry { PlatformId = 1, Username = "Kanzaaa", Level = PermissionLevel.Owner });

        config.DedupePermissions();

        Assert.Equal(PermissionLevel.Owner, config.Permissions.Single().Level);
    }

    [Fact]
    public void The_last_name_seen_is_kept_since_it_is_the_most_recent()
    {
        var config = WithPermissions(
            new PermissionEntry { PlatformId = 1, Username = "Kanza", Level = PermissionLevel.Owner },
            new PermissionEntry { PlatformId = 1, Username = "Kanzaaa", Level = PermissionLevel.Owner });

        config.DedupePermissions();

        Assert.Equal("Kanzaaa", config.Permissions.Single().Username);
    }

    [Fact]
    public void A_blank_later_name_does_not_erase_a_known_one()
    {
        var config = WithPermissions(
            new PermissionEntry { PlatformId = 1, Username = "Kanza", Level = PermissionLevel.Owner },
            new PermissionEntry { PlatformId = 1, Username = "", Level = PermissionLevel.Owner });

        config.DedupePermissions();

        Assert.Equal("Kanza", config.Permissions.Single().Username);
    }

    [Fact]
    public void Different_ids_are_left_alone_even_under_one_name()
    {
        // Two accounts really can share a name, and the ids differ by a digit.
        var config = WithPermissions(
            new PermissionEntry { PlatformId = 76561197970420780, Username = "Terminator", Level = PermissionLevel.Owner },
            new PermissionEntry { PlatformId = 76561197970420789, Username = "Terminator", Level = PermissionLevel.Owner });

        Assert.Equal(0, config.DedupePermissions());
        Assert.Equal(2, config.Permissions.Count);
    }

    [Fact]
    public void A_clean_list_is_left_exactly_as_it_was()
    {
        var config = WithPermissions(
            new PermissionEntry { PlatformId = 1, Username = "a", Level = PermissionLevel.Owner },
            new PermissionEntry { PlatformId = 2, Username = "b", Level = PermissionLevel.Operator });

        Assert.Equal(0, config.DedupePermissions());
        Assert.Equal(2, config.Permissions.Count);
    }

    [Fact]
    public void The_order_of_what_survives_is_kept()
    {
        var config = WithPermissions(
            new PermissionEntry { PlatformId = 1, Username = "first", Level = PermissionLevel.Owner },
            new PermissionEntry { PlatformId = 2, Username = "second", Level = PermissionLevel.Operator },
            new PermissionEntry { PlatformId = 1, Username = "first", Level = PermissionLevel.Owner });

        config.DedupePermissions();

        Assert.Equal(new ulong[] { 1, 2 }, config.Permissions.Select(p => p.PlatformId));
    }
}
