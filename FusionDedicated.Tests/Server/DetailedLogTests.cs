namespace FusionDedicated.Tests.Server;

/// <summary>
/// The detailed log lines for checking the seat and owner fixes in game. They
/// stay off the console, which a busy vehicle would otherwise fill.
/// </summary>
public class DetailedLogTests
{
    private static void LogsQuietly(string signature, string words)
    {
        string body = FusionServerSource.Method(signature);

        int found = body.IndexOf(words, StringComparison.Ordinal);

        Assert.True(found >= 0, $"{signature} no longer logs '{words}'");

        int call = body.LastIndexOf("Log(\"INFO\", ", found, StringComparison.Ordinal);
        int end = body.IndexOf("console: false);", found, StringComparison.Ordinal);
        int next = body.IndexOf("Log(", found, StringComparison.Ordinal);

        Assert.True(call >= 0, $"'{words}' is not in an INFO line");
        Assert.True(end > found && (next < 0 || end < next), $"'{words}' must stay off the console");
    }

    [Fact]
    public void Getting_in_and_out_of_a_seat_is_logged_with_whether_the_entity_is_known()
        => LogsQuietly("private void HandleSeat(", "relay type");

    [Fact]
    public void A_rider_taken_out_for_being_too_far_away_is_logged()
        => LogsQuietly("private void TrackPlayerPose(", "taken out of seat");

    [Fact]
    public void A_seat_replay_is_logged()
        => LogsQuietly("private void ReplaySeats(", "seat(s) in entity");

    [Fact]
    public void The_owner_following_the_rider_is_logged()
        => LogsQuietly("private void TrackEntityPose(", "who sits in it");

    [Fact]
    public void A_redirected_data_request_is_logged()
        => LogsQuietly("private void HandleEntityDataRequest(", "redirected from");

    [Fact]
    public void An_ownership_approval_is_logged()
        => LogsQuietly("private void HandleOwnershipRequest(", "asked for by");
}
