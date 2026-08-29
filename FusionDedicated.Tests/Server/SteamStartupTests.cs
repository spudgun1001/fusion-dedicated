using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class SteamStartupTests
{
    [Fact]
    public void A_successful_init_is_ok()
    {
        Assert.Equal(SteamInitResult.Ok, SteamStartup.TryInit(() => true));
    }

    [Fact]
    public void A_false_return_means_the_client_refused()
    {
        Assert.Equal(SteamInitResult.RefusedByClient, SteamStartup.TryInit(() => false));
    }

    [Fact]
    public void A_missing_native_library_is_reported_rather_than_thrown()
    {
        var result = SteamStartup.TryInit(() => throw new DllNotFoundException("steam_api64"));

        Assert.Equal(SteamInitResult.NativeLibraryMissing, result);
    }

    [Fact]
    public void A_bad_image_format_also_means_the_library_is_unusable()
    {
        var result = SteamStartup.TryInit(() => throw new BadImageFormatException("wrong arch"));

        Assert.Equal(SteamInitResult.NativeLibraryMissing, result);
    }

    [Fact]
    public void An_unrelated_exception_is_not_swallowed()
    {
        Assert.Throws<InvalidOperationException>(
            () => SteamStartup.TryInit(() => throw new InvalidOperationException("something else")));
    }

    [Theory]
    [InlineData(SteamInitResult.RefusedByClient)]
    [InlineData(SteamInitResult.NativeLibraryMissing)]
    public void Every_failure_explains_itself(SteamInitResult result)
    {
        Assert.NotEmpty(SteamStartup.Explain(result));
    }

    [Fact]
    public void The_missing_library_message_names_the_file_to_supply()
    {
        Assert.Contains("libsteam_api.so", SteamStartup.Explain(SteamInitResult.NativeLibraryMissing));
    }
}
