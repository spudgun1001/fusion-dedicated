using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// Phase 3 lets a plugin claim a module tag. This is how anybody finds out what
/// that tag's messages actually contain, without decompiling the mod.
/// </summary>
public class ModuleInspectorTests
{
    [Fact]
    public void Nothing_is_recorded_while_it_is_off()
    {
        var inspector = new ModuleInspector();

        inspector.Note(42, 1, "Joel", new byte[] { 1, 2, 3 });

        Assert.Empty(inspector.Recent);
    }

    [Fact]
    public void A_message_is_recorded_once_it_is_on()
    {
        var inspector = new ModuleInspector { Enabled = true };

        inspector.Note(42, 1, "Joel", new byte[] { 1, 2, 3 });

        var entry = inspector.Recent.Single();

        Assert.Equal(42, entry.HandlerTag);
        Assert.Equal("Joel", entry.Sender);
        Assert.Equal(3, entry.Length);
    }

    [Fact]
    public void The_payload_is_kept_as_hex_so_it_can_be_read_and_pasted()
    {
        var inspector = new ModuleInspector { Enabled = true };

        inspector.Note(42, 1, "Joel", new byte[] { 0x00, 0x0F, 0xFF });

        Assert.Equal("000FFF", inspector.Recent.Single().Payload);
    }

    [Fact]
    public void The_newest_message_is_first()
    {
        var inspector = new ModuleInspector { Enabled = true };

        inspector.Note(1, 1, "a", new byte[] { 1 });
        inspector.Note(2, 1, "b", new byte[] { 2 });

        Assert.Equal(2, inspector.Recent[0].HandlerTag);
    }

    [Fact]
    public void Only_the_most_recent_are_kept_so_a_busy_server_cannot_fill_memory()
    {
        var inspector = new ModuleInspector { Enabled = true };

        for (var i = 0; i < ModuleInspector.Capacity + 20; i++)
        {
            inspector.Note(i, 1, "a", new byte[] { 1 });
        }

        Assert.Equal(ModuleInspector.Capacity, inspector.Recent.Count);
    }

    [Fact]
    public void A_long_payload_is_cut_rather_than_held_whole()
    {
        var inspector = new ModuleInspector { Enabled = true };

        inspector.Note(1, 1, "a", new byte[ModuleInspector.MaxPayloadBytes + 50]);

        var entry = inspector.Recent.Single();

        // The real length is still reported, so a cut is obvious.
        Assert.Equal(ModuleInspector.MaxPayloadBytes + 50, entry.Length);
        Assert.Equal(ModuleInspector.MaxPayloadBytes * 2, entry.Payload.Length);
    }

    [Fact]
    public void Turning_it_off_clears_what_was_held()
    {
        // Otherwise it could be used to read traffic captured before anybody
        // looked, which is not what a debugging tool should allow.
        var inspector = new ModuleInspector { Enabled = true };

        inspector.Note(1, 1, "a", new byte[] { 1 });
        inspector.Enabled = false;

        Assert.Empty(inspector.Recent);
    }

    [Fact]
    public void Turning_it_on_again_starts_from_nothing()
    {
        var inspector = new ModuleInspector { Enabled = true };

        inspector.Note(1, 1, "a", new byte[] { 1 });
        inspector.Enabled = false;
        inspector.Enabled = true;

        Assert.Empty(inspector.Recent);
    }
}
