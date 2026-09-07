using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A player's metadata and cosmetics come from their own client, are written
/// from two threads, and are repeated to everybody who joins afterwards. So an
/// unbounded one is paid for on every future join, and an unguarded one corrupts.
/// </summary>
public class PlayerStateLimitTests
{
    private static ConnectedPlayer Player() => new()
    {
        Connection = new HSteamNetConnection(1),
        PlatformId = 76561198000000001,
        SmallId = 3,
        Username = "Kanzaaa",
    };

    [Fact]
    public void A_key_is_set_and_read_back()
    {
        var player = Player();
        player.SetMetadata("Nickname", "Kanza");

        Assert.Equal("Kanza", player.Metadata["Nickname"]);
    }

    [Fact]
    public void Reading_gives_a_copy_so_a_caller_cannot_write_through_it()
    {
        // The property used to be the dictionary itself, so two threads wrote it
        // directly. Handing out a copy is what stops that, and it means an
        // assignment through the property does nothing, which is why every
        // caller was changed to SetMetadata.
        var player = Player();
        player.SetMetadata("Nickname", "Kanza");

        player.Metadata["Nickname"] = "somebody else";

        Assert.Equal("Kanza", player.Metadata["Nickname"]);
    }

    [Fact]
    public void There_is_a_ceiling_on_how_many_keys_are_held()
    {
        var player = Player();

        for (int i = 0; i < 500; i++)
        {
            player.SetMetadata($"key{i}", "value");
        }

        Assert.InRange(player.Metadata.Count, 1, 64);
    }

    [Fact]
    public void A_key_already_held_can_still_be_changed_at_the_ceiling()
    {
        // Otherwise a flood would freeze everybody's real values.
        var player = Player();

        for (int i = 0; i < 500; i++)
        {
            player.SetMetadata($"key{i}", "value");
        }

        string existing = player.Metadata.Keys.First();
        player.SetMetadata(existing, "changed");

        Assert.Equal("changed", player.Metadata[existing]);
    }

    [Fact]
    public void An_enormous_key_or_value_is_refused()
    {
        var player = Player();

        player.SetMetadata(new string('k', 5000), "value");
        player.SetMetadata("Nickname", new string('v', 5000));

        Assert.Empty(player.Metadata);
    }

    [Fact]
    public void A_cosmetic_goes_on_and_comes_off()
    {
        var player = Player();

        player.SetEquipped("Pack.PointItem.Hat", true);
        Assert.Contains("Pack.PointItem.Hat", player.EquippedItems);

        player.SetEquipped("Pack.PointItem.Hat", false);
        Assert.Empty(player.EquippedItems);
    }

    [Fact]
    public void The_same_cosmetic_twice_is_held_once()
    {
        var player = Player();

        player.SetEquipped("Pack.PointItem.Hat", true);
        player.SetEquipped("Pack.PointItem.Hat", true);

        Assert.Single(player.EquippedItems);
    }

    [Fact]
    public void There_is_a_ceiling_on_cosmetics_too()
    {
        // Unbounded, this was also quadratic: every message searched the list.
        var player = Player();

        for (int i = 0; i < 500; i++)
        {
            player.SetEquipped($"Pack.PointItem.Hat{i}", true);
        }

        Assert.InRange(player.EquippedItems.Count, 1, 128);
    }

    [Fact]
    public void Writing_from_many_threads_at_once_does_not_corrupt_it()
    {
        var player = Player();
        var faults = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                player.SetMetadata("Nickname", $"name{i}");
                player.SetEquipped($"Pack.PointItem.Hat{i % 20}", i % 2 == 0);

                _ = player.Metadata.Count;
                _ = player.EquippedItems.Count;
            }
            catch (Exception e)
            {
                faults.Add(e);
            }
        });

        Assert.Empty(faults);
    }
}
