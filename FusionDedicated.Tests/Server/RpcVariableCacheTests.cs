using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The latest value of every RPC variable, replayed to somebody who joins. A
/// despawned prop's values used to stay for the life of the level, so a busy level
/// filled the cache and newer props stopped being replayed at all.
/// </summary>
public class RpcVariableCacheTests
{
    private const byte RpcBool = 212;

    /// <summary>A component path as a message names it: has entity, entity, component, then a hash or not.</summary>
    private static byte[] EntityPath(ushort entity, ushort component, bool withHash = false)
    {
        var path = new List<byte>
        {
            1,
            (byte)(entity >> 8), (byte)entity,
            (byte)(component >> 8), (byte)component,
            (byte)(withHash ? 1 : 0),
        };

        if (withHash)
        {
            path.AddRange(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        }

        return path.ToArray();
    }

    /// <summary>A variable that came with the level, named by its hierarchy hash alone.</summary>
    private static byte[] LevelPath() => new byte[] { 0, 0, 0, 0, 0, 1, 1, 2, 3, 4, 5, 6, 7, 8 };

    private static byte[] Body(byte[] path, byte value) => path.Concat(new[] { value }).ToArray();

    private static void Hold(RpcVariableCache cache, byte[] path, byte value = 1, byte from = 1)
        => cache.Set(RpcBool, from, Body(path, value), path);

    [Fact]
    public void A_despawned_props_variables_are_forgotten()
    {
        var cache = new RpcVariableCache();
        Hold(cache, EntityPath(300, 6));
        Hold(cache, EntityPath(301, 6));

        Assert.Equal(1, cache.ForgetEntity(300));

        var left = Assert.Single(cache.All());
        Assert.Equal(EntityPath(301, 6), left.Body[..6]);
    }

    [Fact]
    public void Every_variable_on_the_prop_goes()
    {
        var cache = new RpcVariableCache();
        Hold(cache, EntityPath(300, 1));
        Hold(cache, EntityPath(300, 6));
        Hold(cache, EntityPath(300, 8, withHash: true));

        Assert.Equal(3, cache.ForgetEntity(300));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void A_levels_own_variables_are_left_alone()
    {
        var cache = new RpcVariableCache();
        Hold(cache, LevelPath());
        Hold(cache, EntityPath(300, 6));

        cache.ForgetEntity(300);

        var left = Assert.Single(cache.All());
        Assert.Equal(LevelPath(), left.Body[..14]);
    }

    [Fact]
    public void A_players_copy_with_a_hash_and_the_servers_without_are_one_variable()
    {
        // A player's game names the component with its hierarchy hash on the end and
        // the server does not, so they were held twice and a joiner could be sent the
        // stale one after the right one.
        var cache = new RpcVariableCache();
        Hold(cache, EntityPath(300, 6, withHash: true), value: 0, from: 3);
        Hold(cache, EntityPath(300, 6), value: 1, from: 0);

        var held = Assert.Single(cache.All());
        Assert.Equal((byte)1, held.Body[^1]);
    }

    [Fact]
    public void A_full_cache_takes_a_new_variable_once_a_prop_is_forgotten()
    {
        var cache = new RpcVariableCache();

        for (int i = 0; i < RpcVariableCache.MaxVariables; i++)
        {
            Hold(cache, EntityPath((ushort)(EntityRegistry.FirstEntityId + i), 6));
        }

        var extra = EntityPath(9000, 6);
        Assert.False(cache.Set(RpcBool, 1, Body(extra, 1), extra));

        cache.ForgetEntity(EntityRegistry.FirstEntityId);

        Assert.True(cache.Set(RpcBool, 1, Body(extra, 1), extra));
    }

    [Fact]
    public void A_full_cache_still_updates_a_variable_it_holds()
    {
        var cache = new RpcVariableCache();

        for (int i = 0; i < RpcVariableCache.MaxVariables; i++)
        {
            Hold(cache, EntityPath((ushort)(EntityRegistry.FirstEntityId + i), 6));
        }

        var held = EntityPath(EntityRegistry.FirstEntityId, 6);

        Assert.True(cache.Set(RpcBool, 1, Body(held, 0), held));
    }

    [Fact]
    public void One_props_variables_can_be_read_on_their_own()
    {
        // A client that has just spawned a prop is sent that prop's values, not the level's.
        var cache = new RpcVariableCache();
        Hold(cache, EntityPath(300, 1), value: 7);
        Hold(cache, EntityPath(300, 3, withHash: true), value: 8);
        Hold(cache, EntityPath(3001, 1));
        Hold(cache, LevelPath());

        var mine = cache.ForEntity(300);

        Assert.Equal(2, mine.Count);
        Assert.All(mine, v => Assert.Equal(EntityPath(300, 0)[..3], v.Body[..3]));
    }

    [Fact]
    public void A_prop_with_no_variables_has_nothing_to_send()
        => Assert.Empty(new RpcVariableCache().ForEntity(300));
}
