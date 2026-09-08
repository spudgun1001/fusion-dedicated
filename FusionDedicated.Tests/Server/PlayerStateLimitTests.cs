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

/// <summary>
/// Amplifiers: one small message from one client costing the server, or another
/// player, a great deal more than it cost to send.
/// </summary>
public class AmplificationTests
{
    [Fact]
    public void A_target_named_many_times_is_sent_to_once()
    {
        // Naming somebody 255 times had the server send them 255 copies of
        // whatever was attached, which could be the whole message size.
        var message = new BonelabServerBrowser.Fusion.FusionNetWriter(64);

        message.Write((byte)200);
        message.Write((byte)5);     // ToTargets
        message.Write((byte)0);
        message.WriteBlock(new byte[] { 7, 7, 7, 7, 7, 3, 7, 3 });
        message.WriteNullable((byte)1);
        message.WriteBlock(new byte[] { 9 });

        var targets = FusionDedicated.Protocol.ServerProtocol.ReadTargets(message.ToArray());

        Assert.Equal(new byte[] { 7, 3 }, targets);
    }

    [Fact]
    public void An_apostrophe_in_a_name_cannot_close_a_handler_string()
    {
        // A name lands inside onclick="kick(1, 'name')" in the panel, so one
        // apostrophe closed the string and the rest ran as script for whoever
        // had the panel open.
        string page = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "FusionDedicated", "Web", "index.html"));

        int start = page.IndexOf("const esc =", StringComparison.Ordinal);

        Assert.True(start > 0, "the escaping helper moved");

        string helper = page[start..(start + 200)];

        Assert.Contains("&#39;", helper);
    }
}

/// <summary>
/// A player picks their own name and BONELAB lets them colour it, so what reaches
/// the panel is often a rope of colour tags wrapped around one letter each. The
/// panel escaped it, which is safe, but it was drawn in full and a name with no
/// spaces in it pushed the table wider than the window and took the header and
/// the whole page sideways with it.
/// </summary>
public class PanelNameTests
{
    private static string Page() => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "FusionDedicated", "Web", "index.html"));

    [Fact]
    public void Rich_text_is_taken_out_of_a_name_before_it_is_drawn()
    {
        string page = Page();

        Assert.Contains("function plain(value, limit)", page);
        Assert.Contains("replace(/<[^>]*>/g, '')", page);
    }

    [Fact]
    public void Every_place_a_name_is_drawn_goes_through_it()
    {
        // Escaping alone was what let the tags through as text.
        string page = Page();

        Assert.DoesNotContain("esc(p.name)", page);
        Assert.DoesNotContain("esc(cell)", page);
        Assert.DoesNotContain("esc(o.label)", page);
    }

    [Fact]
    public void Nothing_long_can_widen_a_table()
    {
        Assert.Contains("overflow-wrap: anywhere", Page());
    }

    [Fact]
    public void The_page_holds_no_control_characters_of_its_own()
    {
        // A regex written as an escape has to reach the browser as an escape. One
        // built in the wrong place put a real NUL in the file instead.
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "FusionDedicated", "Web", "index.html"));

        Assert.DoesNotContain(bytes, b => b < 9 || (b > 13 && b < 32) || b == 127);
    }
}
