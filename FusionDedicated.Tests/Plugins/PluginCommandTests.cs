using FusionDedicated;
using FusionDedicated.Commands;

namespace FusionDedicated.Tests.Plugins;

public class PluginCommandTests
{
    private sealed class FakeTarget : ICommandTarget
    {
        public IReadOnlyList<string> Plugins = Array.Empty<string>();
        public int ReloadResult;

        public IReadOnlyList<CommandPlayer> Players => Array.Empty<CommandPlayer>();

        public void SetRank(ulong platformId, string name, PermissionLevel level) { }
        public void Kick(byte smallId, string reason) { }
        public void Ban(ulong platformId, string name, string reason, TimeSpan? duration) { }
        public void Mute(ulong platformId, string name) { }
        public void Unmute(ulong platformId, string name) { }
        public bool Unban(ulong platformId) => false;
        public int Purge(byte smallId) => 0;
        public void SetLevel(string barcode, string title) { }

        public IReadOnlyList<string> ListPlugins() => Plugins;
        public int ReloadPlugins() => ReloadResult;
    }

    [Fact]
    public void The_plugins_command_lists_what_is_loaded()
    {
        var processor = new CommandProcessor(new FakeTarget { Plugins = new[] { "police 1.0.0" } });

        Assert.Contains("police", processor.Execute("plugins"));
    }

    [Fact]
    public void The_plugins_command_says_so_when_there_are_none()
    {
        var processor = new CommandProcessor(new FakeTarget());

        Assert.Contains("no plugins", processor.Execute("plugins").ToLowerInvariant());
    }

    [Fact]
    public void Reload_reports_how_many_started()
    {
        var processor = new CommandProcessor(new FakeTarget { ReloadResult = 2 });

        Assert.Contains("2", processor.Execute("plugins reload"));
    }
}
