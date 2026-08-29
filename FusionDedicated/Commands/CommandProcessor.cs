namespace FusionDedicated.Commands;

/// <summary>
/// Parses one command line and carries it out. Commands arrive from stdin or RCON
/// and carry no rank, because whoever reaches either already controls the process —
/// which is also the only way to grant Owner, since no in-game path can.
/// </summary>
public sealed class CommandProcessor
{
    private const string Usage = """
        promote <who> <guest|default|operator|owner>
        kick <who> [reason]
        ban <who> [reason]
        unban <steamid>
        purge <who>
        players
        level <barcode> [title]
        help
        """;

    private readonly ICommandTarget _target;

    public CommandProcessor(ICommandTarget target)
    {
        _target = target;
    }

    public string Execute(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "";
        }

        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant();
        var args = parts.Skip(1).ToArray();

        return command switch
        {
            "promote" => Promote(args),
            "kick" => Kick(args),
            "ban" => Ban(args),
            "unban" => Unban(args),
            "purge" => Purge(args),
            "players" => ListPlayers(),
            "level" => Level(args),
            "help" => Usage,
            _ => $"Unknown command '{parts[0]}'. Type help for the list.",
        };
    }

    private string Promote(string[] args)
    {
        if (args.Length < 2)
        {
            return "Usage: promote <who> <guest|default|operator|owner>";
        }

        if (!TryParseRank(args[^1], out var rank))
        {
            return $"'{args[^1]}' is not a rank. Use guest, default, operator or owner.";
        }

        string who = string.Join(' ', args[..^1]);
        var resolved = Resolve(who);

        if (resolved.Error != null)
        {
            return resolved.Error;
        }

        _target.SetRank(resolved.PlatformId, resolved.Name, rank);

        return resolved.Connected
            ? $"{resolved.Name} is now {rank}."
            : $"{resolved.PlatformId} set to {rank}; it applies at their next join.";
    }

    private string Kick(string[] args)
    {
        if (args.Length < 1)
        {
            return "Usage: kick <who> [reason]";
        }

        var resolved = Resolve(args[0]);

        if (resolved.Error != null)
        {
            return resolved.Error;
        }

        if (!resolved.Connected)
        {
            return $"{args[0]} is not connected.";
        }

        string reason = args.Length > 1 ? string.Join(' ', args[1..]) : "Kicked by an administrator";
        _target.Kick(resolved.SmallId, reason);

        return $"Kicked {resolved.Name}: {reason}";
    }

    private string Ban(string[] args)
    {
        if (args.Length < 1)
        {
            return "Usage: ban <who> [reason]";
        }

        var resolved = Resolve(args[0]);

        if (resolved.Error != null)
        {
            return resolved.Error;
        }

        string reason = args.Length > 1 ? string.Join(' ', args[1..]) : "Banned by an administrator";
        _target.Ban(resolved.PlatformId, resolved.Name, reason);

        return $"Banned {resolved.Name}: {reason}";
    }

    private string Unban(string[] args)
    {
        if (args.Length < 1)
        {
            return "Usage: unban <steamid>";
        }

        if (!ulong.TryParse(args[0], out ulong id))
        {
            return "unban needs a SteamID64, because the player is not connected to look up by name.";
        }

        return _target.Unban(id) ? $"Unbanned {id}." : $"{id} was not banned.";
    }

    private string Purge(string[] args)
    {
        if (args.Length < 1)
        {
            return "Usage: purge <who>";
        }

        var resolved = Resolve(args[0]);

        if (resolved.Error != null)
        {
            return resolved.Error;
        }

        if (!resolved.Connected)
        {
            return $"{args[0]} is not connected.";
        }

        return $"Removed {_target.Purge(resolved.SmallId)} entities belonging to {resolved.Name}.";
    }

    private string ListPlayers()
    {
        if (_target.Players.Count == 0)
        {
            return "Nobody is connected.";
        }

        return string.Join(Environment.NewLine, _target.Players.Select(p =>
            $"  {p.Name} ({p.PlatformId}) {p.Rank} — {p.EntityCount} entities"));
    }

    private string Level(string[] args)
    {
        if (args.Length < 1)
        {
            return "Usage: level <barcode> [title]";
        }

        string title = args.Length > 1 ? string.Join(' ', args[1..]) : args[0];
        _target.SetLevel(args[0], title);

        return $"Level set to {args[0]}.";
    }

    private static bool TryParseRank(string text, out PermissionLevel rank)
    {
        switch (text.ToLowerInvariant())
        {
            case "guest": rank = PermissionLevel.Guest; return true;
            case "default": rank = PermissionLevel.Default; return true;
            case "operator": rank = PermissionLevel.Operator; return true;
            case "owner": rank = PermissionLevel.Owner; return true;
            default: rank = PermissionLevel.Default; return false;
        }
    }

    /// <summary>
    /// Turns a SteamID or a name into a player. A name matching more than one person
    /// is refused rather than guessed at.
    /// </summary>
    private Resolution Resolve(string who)
    {
        if (ulong.TryParse(who, out ulong id))
        {
            var connected = _target.Players.FirstOrDefault(p => p.PlatformId == id);

            return connected is null
                ? new Resolution { PlatformId = id, Name = "" }
                : new Resolution
                {
                    PlatformId = id,
                    Name = connected.Name,
                    SmallId = connected.SmallId,
                    Connected = true,
                };
        }

        var matches = _target.Players
            .Where(p => p.Name.Contains(who, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return new Resolution { Error = $"No connected player matches '{who}'." };
        }

        if (matches.Count > 1)
        {
            string names = string.Join(", ", matches.Select(m => m.Name));
            return new Resolution { Error = $"'{who}' is ambiguous: {names}." };
        }

        return new Resolution
        {
            PlatformId = matches[0].PlatformId,
            Name = matches[0].Name,
            SmallId = matches[0].SmallId,
            Connected = true,
        };
    }

    private sealed class Resolution
    {
        public ulong PlatformId { get; init; }
        public string Name { get; init; } = "";
        public byte SmallId { get; init; }
        public bool Connected { get; init; }
        public string? Error { get; init; }
    }
}
