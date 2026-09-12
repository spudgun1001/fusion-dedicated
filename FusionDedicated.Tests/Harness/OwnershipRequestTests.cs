using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A LabFusion client only ever asks ownership for itself. A request naming
/// somebody else is spoofed, and granting it can freeze an entity for everyone.
/// </summary>
public class OwnershipRequestTests
{
    private const ushort Crate = 300;

    private static byte[] SpoofedOwnershipRequest(byte senderSmallId, byte namedOwner, ushort entityId)
    {
        byte[] message = FusionProtocol.BuildOwnershipRequest(senderSmallId, entityId);
        message[9] = namedOwner;
        return message;
    }

    [Fact]
    public void A_request_naming_somebody_else_is_refused()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        var dennis = world.Join(76561198000000003, "Dennis");
        joel.FinishLoading();
        kanza.FinishLoading();
        dennis.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);

        kanza.Send(SpoofedOwnershipRequest(kanza.SmallId, joel.SmallId, Crate));
        kanza.Send(SpoofedOwnershipRequest(kanza.SmallId, dennis.SmallId, Crate));

        Assert.Equal(joel.SmallId, world.Server.Entities.Get(Crate)!.OwnerSmallId);

        bool dennisAnnounced = world.Transport.SentTo(dennis.Connection).Any(sent =>
        {
            var response = FusionProtocol.TryReadOwnershipResponse(sent.Message);
            return response.HasValue && response.Value.EntityId == Crate && response.Value.PlayerId == dennis.SmallId;
        });

        Assert.False(dennisAnnounced);

        Assert.Equal(1, world.Server.RecentLog(2000)
            .Count(e => e.Message.Contains("Refused an ownership request")));
    }

    [Fact]
    public void A_normal_request_for_yourself_still_works()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);
        kanza.Send(FusionProtocol.BuildOwnershipRequest(kanza.SmallId, Crate));

        Assert.Equal(kanza.SmallId, world.Server.Entities.Get(Crate)!.OwnerSmallId);
    }
}
