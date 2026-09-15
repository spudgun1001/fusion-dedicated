namespace FusionDedicated.Tests.Harness;

/// <summary>
/// An ownerless prop goes to whoever has been here longest, the least likely to leave
/// next, and not to the lowest SmallId. An adopted prop is marked inherited, and a
/// plugin spawn is marked as a plugin spawn only.
/// </summary>
public class AdoptionTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;
    private const ulong LateId = 76561198000000003;

    [Fact]
    public void Adoption_picks_the_longest_joined_player_and_marks_the_entity_inherited()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");

        // Kanza has been here longer, though Joel has the lower SmallId.
        world.Server.Players.Get(kanza.SmallId)!.JoinedAt =
            world.Server.Players.Get(joel.SmallId)!.JoinedAt - TimeSpan.FromMinutes(10);

        var kept = world.Server.Entities.Register(900, "spudgun1001.Payphone.Spawnable.PayphoneWall", 0, 1f, 2f, 3f);
        kept.Persistent = true;
        world.Server.Entities.SetOwner(900, null);

        world.Join(LateId, "Late");

        var entity = world.Server.Entities.Get(900)!;
        Assert.Equal((byte?)kanza.SmallId, entity.OwnerSmallId);
        Assert.True(entity.Inherited);
    }

    [Fact]
    public void A_plugin_spawn_goes_to_the_longest_joined_player_and_is_not_inherited()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        world.Server.Players.Get(kanza.SmallId)!.JoinedAt =
            world.Server.Players.Get(joel.SmallId)!.JoinedAt - TimeSpan.FromMinutes(10);

        ushort id = world.Server.SpawnForPlugin("Pack.Spawnable.Crate", 0f, 0f, 0f, Array.Empty<byte>());
        world.Sync();

        var entity = world.Server.Entities.Get(id)!;
        Assert.Equal((byte?)kanza.SmallId, entity.OwnerSmallId);
        Assert.True(entity.PluginSpawned);
        Assert.False(entity.Inherited);
        Assert.Equal((byte?)kanza.SmallId, joel.View.Entities[id].Owner);
    }

    [Fact]
    public void A_plugin_spawn_made_with_nobody_here_is_owned_but_not_inherited_once_a_joiner_adopts_it()
    {
        using var world = new World();

        ushort id = world.Server.SpawnForPlugin("Pack.Spawnable.Crate", 0f, 0f, 0f, Array.Empty<byte>());
        var entity = world.Server.Entities.Get(id)!;

        Assert.Null(entity.OwnerSmallId);
        Assert.False(entity.Inherited);

        var joel = world.Join(JoelId, "Joel");

        Assert.Equal((byte?)joel.SmallId, entity.OwnerSmallId);
        Assert.False(entity.Inherited);
        Assert.True(entity.PluginSpawned);
    }
}
