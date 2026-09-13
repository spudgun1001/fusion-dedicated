namespace FusionDedicated.Server;

/// <summary>A constraint kept for joiners, with what its ends held when it was made.</summary>
public sealed class StoredConstraint
{
    private StoredConstraint(byte owner, byte[] payload, ushort partner,
        IReadOnlyDictionary<byte, ulong> players, IReadOnlyList<ushort> props, bool readable)
    {
        Owner = owner;
        Payload = payload;
        Partner = partner;
        Players = players;
        Props = props;
        Readable = readable;
    }

    public byte Owner { get; }

    public byte[] Payload { get; }

    /// <summary>The second end's id. The first is the key it is stored under.</summary>
    public ushort Partner { get; }

    /// <summary>Each player end's SmallID, and the player who held it when the constraint was made, or 0 for nobody.</summary>
    public IReadOnlyDictionary<byte, ulong> Players { get; }

    public IReadOnlyList<ushort> Props { get; }

    /// <summary>False when its ends could not be read, so it is kept until it is cleared or the level changes.</summary>
    public bool Readable { get; }

    public static StoredConstraint For(byte owner, byte[] payload, ushort partner,
        (ConstraintEnd First, ConstraintEnd Second)? ends, Func<byte, ulong?> platformOf)
    {
        var players = new Dictionary<byte, ulong>();
        var props = new List<ushort>();

        if (ends is { } both)
        {
            foreach (var end in new[] { both.First, both.Second })
            {
                if (end.IsPlayer)
                {
                    players[(byte)end.EntityId] = platformOf((byte)end.EntityId) ?? 0;
                }
                else if (end.IsProp && !props.Contains(end.EntityId))
                {
                    props.Add(end.EntityId);
                }
            }
        }

        return new StoredConstraint(owner, payload, partner, players, props, ends != null);
    }

    public bool NamesPlayer(byte smallId) => Players.ContainsKey(smallId);

    public bool NamesProp(ushort entityId) => Props.Contains(entityId);

    /// <summary>Whether what it held has gone: a player end's SmallID now held by somebody else or nobody, or a prop end no longer tracked.</summary>
    public bool IsStale(Func<byte, ulong?> platformOf, Func<ushort, bool> exists)
        => Players.Any(player => platformOf(player.Key) is not { } now || now != player.Value)
           || Props.Any(id => !exists(id));
}
