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

    private readonly Dictionary<(byte Tag, string Key), (byte From, byte[] Body)> _values = new();
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

            _values[key] = (from, body);
            return true;
        }
    }

    /// <summary>Whether this exact value is already held, so sending it again would change nothing.</summary>
    public bool IsUnchanged(byte tag, byte[] body, ReadOnlySpan<byte> path)
    {
        var key = (tag, KeyFor(path));

        lock (_lock)
        {
            return _values.TryGetValue(key, out var held) && held.Body.AsSpan().SequenceEqual(body);
        }
    }

    /// <summary>Drops every variable on one prop, for when it has gone.</summary>
    /// <returns>How many went.</returns>
    public int ForgetEntity(ushort entityId)
    {
        string prefix = EntityPrefix(entityId);

        lock (_lock)
        {
            var doomed = _values.Keys
                .Where(k => k.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            foreach (var key in doomed)
            {
                _values.Remove(key);
            }

            return doomed.Count;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _values.Clear();
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
        string prefix = EntityPrefix(entityId);

        lock (_lock)
        {
            return _values
                .Where(v => v.Key.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(v => (v.Key.Tag, v.Value.From, v.Value.Body))
                .ToList();
        }
    }

    /// <summary>The entity and component for a prop's variable, or the whole path for a level's.</summary>
    private static string KeyFor(ReadOnlySpan<byte> path)
        => path.Length >= 5 && path[0] == 1
            ? EntityPrefix((ushort)((path[1] << 8) | path[2])) + ((path[3] << 8) | path[4])
            : "L" + Convert.ToHexString(path);

    private static string EntityPrefix(ushort entityId) => "E" + entityId + ":";
}
