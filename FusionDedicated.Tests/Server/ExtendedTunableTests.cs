using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The spawn and nickname guards used to switch off entirely when blocklist.json
/// was absent, even with extended protection on, so a server with no file had no
/// limits at all.
/// </summary>
public class ExtendedTunableTests
{
    [Fact]
    public void Defaults_apply_when_there_is_no_blocklist_file()
    {
        var limits = ExtendedLimits.Resolve(extendedProtection: true, file: null);

        Assert.Equal(BuiltInSafety.DefaultMaxSpawnsPerSecond, limits.MaxSpawnsPerSecond);
        Assert.Equal(
            BuiltInSafety.DefaultMaxNicknameChangesPerMinute,
            limits.MaxNicknameChangesPerMinute);
        Assert.Empty(limits.ReservedNicknames);
    }

    [Fact]
    public void File_values_win_over_the_defaults()
    {
        var file = new BlocklistFile
        {
            MaxSpawnsPerSecond = 11,
            MaxNicknameChangesPerMinute = 2,
            ReservedNicknames = { "Owner" },
        };

        var limits = ExtendedLimits.Resolve(extendedProtection: true, file);

        Assert.Equal(11, limits.MaxSpawnsPerSecond);
        Assert.Equal(2, limits.MaxNicknameChangesPerMinute);
        Assert.Equal(new[] { "Owner" }, limits.ReservedNicknames);
    }

    [Fact]
    public void Everything_is_off_when_extended_protection_is_off()
    {
        var file = new BlocklistFile { MaxSpawnsPerSecond = 11, ReservedNicknames = { "Owner" } };

        var limits = ExtendedLimits.Resolve(extendedProtection: false, file);

        Assert.Equal(0, limits.MaxSpawnsPerSecond);
        Assert.Equal(0, limits.MaxNicknameChangesPerMinute);
        Assert.Empty(limits.ReservedNicknames);
    }
}
