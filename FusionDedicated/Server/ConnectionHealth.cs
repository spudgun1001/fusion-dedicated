namespace FusionDedicated.Server;

/// <summary>What Steam measures about one connection. Quality is the share of packets delivered, 0 to 1.</summary>
/// <param name="WaitingBytes">Everything Steam still holds for this connection, buffered or sent and unacknowledged.</param>
/// <param name="PendingBytes">The part Steam has buffered and not yet put on the wire, which is what its send buffer limit counts.</param>
public readonly record struct ConnectionHealth(
    int PingMs,
    float QualityLocal,
    float QualityRemote,
    float OutBytesPerSecond,
    int WaitingBytes,
    long QueueMicroseconds,
    int PendingBytes)
{
    /// <summary>Bad enough that the player is likely to feel it.</summary>
    public bool IsPoor => PingMs > 250
                          || Math.Min(QualityLocal, QualityRemote) < 0.9f
                          || QueueMicroseconds > 500_000;

    public string Describe(string name)
        => $"Net {name}: ping {PingMs} ms, quality {QualityLocal * 100:0}%/{QualityRemote * 100:0}%, " +
           $"out {OutBytesPerSecond / 1000:0} KB/s, waiting {WaitingBytes / 1000f:0.0} KB, " +
           $"queue {QueueMicroseconds / 1000} ms";
}
