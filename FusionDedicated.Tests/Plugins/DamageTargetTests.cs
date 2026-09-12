using System.Buffers.Binary;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// The damage event told plugins nobody was hit: TargetPlatformId was always 0.
/// It should name the player the hit was routed to.
/// </summary>
public class DamageTargetTests
{
    private static PluginEvents Events() => new(new PluginHealth(), (_, _) => { });

    private static byte[] DamagePayload(float damage)
    {
        var payload = new byte[45];
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(0, 4), damage);
        return payload;
    }

    /// <summary>A ToTarget (relay type 4) PlayerRepDamage message: tag, relay type, channel, nullable target, nullable sender, then the length prefixed payload.</summary>
    private static byte[] DamageToTarget(byte senderSmallId, byte? targetSmallId, float damage)
    {
        var buffer = new List<byte> { GateProtocol.TagPlayerRepDamage, 4, 0 };

        if (targetSmallId is { } target)
        {
            buffer.Add(1);
            buffer.Add(target);
        }
        else
        {
            buffer.Add(0);
        }

        buffer.Add(1);
        buffer.Add(senderSmallId);

        byte[] payload = DamagePayload(damage);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);

        buffer.AddRange(length);
        buffer.AddRange(payload);

        return buffer.ToArray();
    }

    [Fact]
    public void A_hit_routed_to_a_player_names_them_as_the_target()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        world.Server.Plugins = Events();
        DamageEvent? captured = null;
        world.Server.Plugins.Damage.Subscribe("test", e =>
        {
            captured = e;
            return PluginVerdict.Allow;
        });

        joel.Send(DamageToTarget(joel.SmallId, kanza.SmallId, 10f));

        Assert.NotNull(captured);
        Assert.Equal(76561198000000002UL, captured!.Value.TargetPlatformId);
    }

    [Fact]
    public void A_hit_with_no_route_target_reports_zero()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Server.Plugins = Events();
        DamageEvent? captured = null;
        world.Server.Plugins.Damage.Subscribe("test", e =>
        {
            captured = e;
            return PluginVerdict.Allow;
        });

        joel.Send(DamageToTarget(joel.SmallId, null, 10f));

        Assert.NotNull(captured);
        Assert.Equal(0UL, captured!.Value.TargetPlatformId);
    }

    [Fact]
    public void A_hit_routed_to_a_small_id_nobody_online_holds_reports_zero()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Server.Plugins = Events();
        DamageEvent? captured = null;
        world.Server.Plugins.Damage.Subscribe("test", e =>
        {
            captured = e;
            return PluginVerdict.Allow;
        });

        joel.Send(DamageToTarget(joel.SmallId, 250, 10f));

        Assert.NotNull(captured);
        Assert.Equal(0UL, captured!.Value.TargetPlatformId);
    }
}
