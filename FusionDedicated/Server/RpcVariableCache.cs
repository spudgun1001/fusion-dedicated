namespace FusionDedicated.Server;

/// <summary>
/// The latest value of every RPC variable, replayed to somebody who joins.
///
/// A variable on a spawned prop is held by its entity and component, so a player's
/// copy that carries the hierarchy hash and the server's copy that does not are one
/// variable and the latest wins. A level's own, named by hash alone, is held by its
/// path. A prop's variables are forgotten when it goes.
/// </summary>
public sealed class RpcVariableCache
{
    /// <summary>
    /// The most to hold. Everything here comes from what clients sent, so without a
    /// ceiling one player could make every future join carry whatever they liked.
    /// </summary>
    public const int MaxVariables = 2048;

    private readonly Dictionary<(byte Tag, string Key), (byte From, byte[] Body, bool Stale, byte[] Path)> _values = new();

    /// <summary>
    /// Which keys belong to which prop. A client asks about one prop at a time and a
    /// full cache holds thousands of the level's own, so finding a prop's few by
    /// walking all of them cost the whole cache on every request.
    /// </summary>
    private readonly Dictionary<ushort, List<(byte Tag, string Key)>> _byEntity = new();

    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) { return _values.Count; } }
    }

    /// <summary>Holds a variable's latest value.</summary>
    /// <returns>False when full and this is not a variable it already holds.</returns>
    public bool Set(byte tag, byte from, byte[] body, ReadOnlySpan<byte> path)
    {
        var key = (tag, KeyFor(path));

        lock (_lock)
        {
            if (_values.Count >= MaxVariables && !_values.ContainsKey(key))
            {
                return false;
            }

            _values[key] = (from, body, false, path.ToArray());
            Index(key, EntityOf(path));
            return true;
        }
    }

    /// <summary>Whether this exact value is already held, so sending it again would change nothing.</summary>
    public bool IsUnchanged(byte tag, byte[] body, ReadOnlySpan<byte> path)
    {
        var key = (tag, KeyFor(path));

        lock (_lock)
        {
            return _values.TryGetValue(key, out var held) && !held.Stale && held.Body.AsSpan().SequenceEqual(body);
        }
    }

    /// <summary>
    /// Marks a held variable stale when a player sent a different value, so the held
    /// one is sent again rather than skipped. It is still what a joiner is told.
    /// </summary>
    /// <returns>Whether it was held and differed.</returns>
    public bool MarkStaleIfDifferent(byte tag, byte[] body, ReadOnlySpan<byte> path)
    {
        var key = (tag, KeyFor(path));

        lock (_lock)
        {
            if (!_values.TryGetValue(key, out var held) || held.Body.AsSpan().SequenceEqual(body))
            {
                return false;
            }

            _values[key] = held with { Stale = true };
            return true;
        }
    }

    /// <summary>Forgets one variable, for a value players were sent that is too big to keep.</summary>
    /// <returns>Whether one was held.</returns>
    public bool Forget(byte tag, ReadOnlySpan<byte> path)
    {
        var key = (tag, KeyFor(path));

        lock (_lock)
        {
            if (!_values.Remove(key))
            {
                return false;
            }

            Unindex(key, EntityOf(path));
            return true;
        }
    }

    /// <summary>Drops every variable on one prop, for when it has gone.</summary>
    /// <returns>How many went.</returns>
    public int ForgetEntity(ushort entityId)
    {
        lock (_lock)
        {
            if (!_byEntity.Remove(entityId, out var keys))
            {
                return 0;
            }

            foreach (var key in keys)
            {
                _values.Remove(key);
            }

            return keys.Count;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _values.Clear();
            _byEntity.Clear();
        }
    }

    /// <summary>Every held value, in the order each variable was first seen.</summary>
    public List<(byte Tag, byte From, byte[] Body)> All()
    {
        lock (_lock)
        {
            return _values.Select(v => (v.Key.Tag, v.Value.From, v.Value.Body)).ToList();
        }
    }

    /// <summary>Every held value on one prop, for a client that has just spawned it.</summary>
    public List<(byte Tag, byte From, byte[] Body)> ForEntity(ushort entityId)
    {
        lock (_lock)
        {
            var held = new List<(byte Tag, byte From, byte[] Body)>();

            foreach (var key in Keys(entityId))
            {
                if (_values.TryGetValue(key, out var value))
                {
                    held.Add((key.Tag, value.From, value.Body));
                }
            }

            return held;
        }
    }

    /// <summary>
    /// Every held value's identity rather than its value, for a paced send that looks up what
    /// is held at the moment it actually goes rather than what was held when it was queued.
    /// </summary>
    public List<(byte Tag, byte[] Path)> AllPaths()
    {
        lock (_lock)
        {
            return _values.Select(v => (v.Key.Tag, v.Value.Path)).ToList();
        }
    }

    /// <summary>Every held value's identity on one prop. See <see cref="AllPaths"/>.</summary>
    public List<(byte Tag, byte[] Path)> EntityPaths(ushort entityId)
    {
        lock (_lock)
        {
            var paths = new List<(byte Tag, byte[] Path)>();

            foreach (var key in Keys(entityId))
            {
                if (_values.TryGetValue(key, out var value))
                {
                    paths.Add((key.Tag, value.Path));
                }
            }

            return paths;
        }
    }

    /// <summary>One prop's keys, oldest first. Call under the lock.</summary>
    private IReadOnlyList<(byte Tag, string Key)> Keys(ushort entityId)
        => _byEntity.TryGetValue(entityId, out var keys) ? keys : Array.Empty<(byte, string)>();

    private void Index((byte Tag, string Key) key, ushort? entityId)
    {
        if (entityId is not { } id)
        {
            return;
        }

        if (!_byEntity.TryGetValue(id, out var keys))
        {
            _byEntity[id] = keys = new List<(byte Tag, string Key)>();
        }

        if (!keys.Contains(key))
        {
            keys.Add(key);
        }
    }

    private void Unindex((byte Tag, string Key) key, ushort? entityId)
    {
        if (entityId is { } id && _byEntity.TryGetValue(id, out var keys)
            && keys.Remove(key) && keys.Count == 0)
        {
            _byEntity.Remove(id);
        }
    }

    /// <summary>The prop a path names, or null when it is one of the level's own.</summary>
    private static ushort? EntityOf(ReadOnlySpan<byte> path)
        => path.Length >= 5 && path[0] == 1 ? (ushort)((path[1] << 8) | path[2]) : null;

    /// <summary>The value currently held for one variable, or null when it has gone.</summary>
    public (byte From, byte[] Body)? Current(byte tag, byte[] path)
    {
        var key = (tag, KeyFor(path));

        lock (_lock)
        {
            return _values.TryGetValue(key, out var held) ? (held.From, held.Body) : null;
        }
    }

    /// <summary>The entity and component for a prop's variable, or the whole path for a level's.</summary>
    private static string KeyFor(ReadOnlySpan<byte> path)
        => path.Length >= 5 && path[0] == 1
            ? EntityPrefix((ushort)((path[1] << 8) | path[2])) + ((path[3] << 8) | path[4])
            : "L" + Convert.ToHexString(path);

    private static string EntityPrefix(ushort entityId) => "E" + entityId + ":";
}
