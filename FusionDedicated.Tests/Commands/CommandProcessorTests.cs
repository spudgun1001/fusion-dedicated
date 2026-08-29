using FusionDedicated;
using FusionDedicated.Commands;

namespace FusionDedicated.Tests.Commands;

public class FakeTarget : ICommandTarget
{
    public List<CommandPlayer> Roster { get; } = new();
    public List<(ulong Id, string Name, PermissionLevel Level)> Ranks { get; } = new();
    public List<(byte SmallId, string Reason)> Kicks { get; } = new();
    public List<(ulong Id, string Name, string Reason)> Bans { get; } = new();
    public List<ulong> Unbans { get; } = new();
    public List<byte> Purges { get; } = new();
    public List<(string Barcode, string Title)> Levels { get; } = new();

    public IReadOnlyList<CommandPlayer> Players => Roster;

    public void SetRank(ulong platformId, string name, PermissionLevel level)
        => Ranks.Add((platformId, name, level));

    public void Kick(byte smallId, string reason) => Kicks.Add((smallId, reason));

    public void Ban(ulong platformId, string name, string reason)
        => Bans.Add((platformId, name, reason));

    public bool Unban(ulong platformId)
    {
        Unbans.Add(platformId);
        return true;
    }

    public int Purge(byte smallId)
    {
        Purges.Add(smallId);
        return 3;
    }

    public void SetLevel(string barcode, string title) => Levels.Add((barcode, title));
}

public class CommandProcessorTests
{
    private readonly FakeTarget _target = new();
    private readonly CommandProcessor _processor;

    public CommandProcessorTests()
    {
        _processor = new CommandProcessor(_target);
        _target.Roster.Add(new CommandPlayer(76561198000000000, 1, "Spudgun", PermissionLevel.Default, 4));
        _target.Roster.Add(new CommandPlayer(76561198000000001, 2, "Mate", PermissionLevel.Default, 0));
    }

    [Fact]
    public void Promote_by_steam_id_sets_the_rank()
    {
        _processor.Execute("promote 76561198000000000 owner");

        Assert.Equal((76561198000000000UL, "Spudgun", PermissionLevel.Owner), _target.Ranks.Single());
    }

    [Fact]
    public void Promote_by_name_is_case_insensitive()
    {
        _processor.Execute("promote spudgun operator");

        Assert.Equal(PermissionLevel.Operator, _target.Ranks.Single().Level);
    }

    [Fact]
    public void Promote_accepts_an_id_for_nobody_connected()
    {
        string reply = _processor.Execute("promote 76561190000000000 operator");

        Assert.Single(_target.Ranks);
        Assert.Contains("next join", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_ambiguous_name_is_refused()
    {
        _target.Roster.Add(new CommandPlayer(3, 3, "Spudgun2", PermissionLevel.Default, 0));

        string reply = _processor.Execute("promote spudgun operator");

        Assert.Empty(_target.Ranks);
        Assert.Contains("ambiguous", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_rank_is_refused_rather_than_defaulting()
    {
        string reply = _processor.Execute("promote spudgun admiral");

        Assert.Empty(_target.Ranks);
        Assert.Contains("admiral", reply);
    }

    [Fact]
    public void Kick_passes_the_reason_through()
    {
        _processor.Execute("kick spudgun being a nuisance");

        Assert.Equal(((byte)1, "being a nuisance"), _target.Kicks.Single());
    }

    [Fact]
    public void Kick_without_a_reason_still_works()
    {
        _processor.Execute("kick spudgun");

        Assert.Equal(1, _target.Kicks.Single().SmallId);
    }

    [Fact]
    public void Ban_and_unban_reach_the_target()
    {
        _processor.Execute("ban spudgun cheating");
        _processor.Execute("unban 76561198000000000");

        Assert.Equal("cheating", _target.Bans.Single().Reason);
        Assert.Equal(76561198000000000UL, _target.Unbans.Single());
    }

    [Fact]
    public void Unban_requires_a_steam_id()
    {
        string reply = _processor.Execute("unban spudgun");

        Assert.Empty(_target.Unbans);
        Assert.Contains("SteamID", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Players_lists_the_roster()
    {
        string reply = _processor.Execute("players");

        Assert.Contains("Spudgun", reply);
        Assert.Contains("Mate", reply);
    }

    [Fact]
    public void Purge_reports_how_many_went()
    {
        Assert.Contains("3", _processor.Execute("purge spudgun"));
    }

    [Fact]
    public void Level_takes_a_barcode_and_optional_title()
    {
        _processor.Execute("level Author.Pallet.Level.Name Some Title");

        Assert.Equal(("Author.Pallet.Level.Name", "Some Title"), _target.Levels.Single());
    }

    [Fact]
    public void Help_lists_the_commands()
    {
        string reply = _processor.Execute("help");

        Assert.Contains("promote", reply);
        Assert.Contains("kick", reply);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_is_ignored(string line)
    {
        Assert.Equal("", _processor.Execute(line));
    }

    [Fact]
    public void An_unknown_command_says_so_rather_than_staying_silent()
    {
        string reply = _processor.Execute("frobnicate everything");

        Assert.Contains("frobnicate", reply);
        Assert.Contains("help", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_command_missing_its_arguments_explains_the_usage()
    {
        Assert.Contains("usage", _processor.Execute("promote"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Commands_are_case_insensitive()
    {
        _processor.Execute("PROMOTE spudgun OWNER");

        Assert.Equal(PermissionLevel.Owner, _target.Ranks.Single().Level);
    }
}
