using BonelabServerBrowser.Fusion;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Server;

/// <summary>Whether a player is close enough to somebody to be sent their voice.</summary>
public static class VoiceReach
{
    /// <summary>
    /// Fusion mutes a voice past 30 m times the square root of the speaker's height over 1.76 m,
    /// with a fifth of the squared distance to spare, so the range grows the same way. Anybody loading
    /// or with no known position is sent everything.
    /// </summary>
    public static bool Reaches(ConnectedPlayer talker, ConnectedPlayer listener, float range)
    {
        if (range <= 0 || talker.Loading || listener.Loading || !talker.HasPosition || !listener.HasPosition)
        {
            return true;
        }

        var a = talker.LastPosition;
        var b = listener.LastPosition;

        return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z).Magnitude <= ReachOf(talker, range);
    }

    /// <summary>How far this talker carries: a taller avatar further, as Fusion plays it.</summary>
    public static float ReachOf(ConnectedPlayer talker, float range)
    {
        float scale = AvatarStatsCheck.HeightOf(talker.AvatarStats) / AvatarStatsCheck.CalibrationHeight;
        return scale > 1f ? range * MathF.Sqrt(scale) : range;
    }
}

/// <summary>
/// Whose voice goes into a voice proxy, from Fusion's VoiceProxyInputMessage as clients are sent it.
/// A radio, a phone call or a megaphone plays that voice wherever the other end is.
/// </summary>
public sealed class VoiceProxyInputs
{
    public static readonly long Tag = ModuleProtocol.TagFor("LabFusion", "LabFusion.SDK.Messages.VoiceProxyInputMessage");

    private readonly Dictionary<string, byte> _inputs = new();

    /// <summary>Reads a module message on its way to a client. The payload ends with the player and on or off.</summary>
    public void Note(byte[] message)
    {
        if (message.Length == 0 || message[0] != ModuleProtocol.TagModule
            || ModuleProtocol.TryReadHandlerTag(message) != Tag
            || ModuleProtocol.TryReadHandlerPayload(message) is not { Length: >= 3 } payload)
        {
            return;
        }

        string proxy = Convert.ToHexString(payload, 0, payload.Length - 2);

        lock (_inputs)
        {
            if (payload[^1] != 0)
            {
                _inputs[proxy] = payload[^2];
            }
            else
            {
                _inputs.Remove(proxy);
            }
        }
    }

    public bool Feeds(byte player)
    {
        lock (_inputs)
        {
            return _inputs.ContainsValue(player);
        }
    }

    public void Forget(byte player)
    {
        lock (_inputs)
        {
            foreach (string proxy in _inputs.Where(i => i.Value == player).Select(i => i.Key).ToList())
            {
                _inputs.Remove(proxy);
            }
        }
    }

    public void Clear()
    {
        lock (_inputs)
        {
            _inputs.Clear();
        }
    }
}
