using System.Diagnostics;

namespace FusionDedicated.Server;

/// <summary>
/// Samples what the server costs to run, and keeps a rolling history so the panel
/// can draw it. Everything here is read straight from the process and, on Linux,
/// from /proc — no counters library, nothing to install on the host.
/// </summary>
public sealed class ResourceMonitor
{
    public sealed record Sample(
        DateTime At,
        double CpuPercent,
        long WorkingSetBytes,
        long ManagedBytes,
        int Threads,
        int Players,
        int Entities,
        long PacketsIn,
        long PacketsOut,
        long BytesIn,
        long BytesOut);

    private readonly Process _process = Process.GetCurrentProcess();
    private readonly List<Sample> _history = new();
    private readonly object _lock = new();

    private TimeSpan _lastCpu = TimeSpan.Zero;
    private DateTime _lastSampledAt = DateTime.UtcNow;
    private long _lastPacketsIn, _lastPacketsOut, _lastBytesIn, _lastBytesOut;

    /// <summary>Roughly two hours at one sample every 5s.</summary>
    private const int MaxSamples = 1440;

    /// <summary>
    /// Minute-resolution rows kept on disk. The in-memory ring only spans a couple of
    /// hours and dies with the process, so anything asking for a day or more reads
    /// from here instead — which also means the graphs survive a restart.
    /// </summary>
    private readonly List<Sample> _minutes = new();

    private string? _metricsPath;
    private DateTime _minuteBucket = DateTime.MinValue;

    /// <summary>About forty days of minute rows.</summary>
    private const int MaxMinutes = 60 * 24 * 40;

    public Sample? Latest { get; private set; }

    /// <summary>Per-second rates worked out between the last two samples.</summary>
    public double PacketsInPerSecond { get; private set; }
    public double PacketsOutPerSecond { get; private set; }
    public double BytesInPerSecond { get; private set; }
    public double BytesOutPerSecond { get; private set; }

    public IReadOnlyList<Sample> History(int count)
    {
        lock (_lock)
        {
            return _history.TakeLast(count).ToList();
        }
    }

    /// <summary>
    /// Points covering the requested span, reduced to at most <paramref name="maxPoints"/>.
    /// Short spans come from the live 5s ring; anything longer reads the minute rows,
    /// which are the only thing that outlives the process.
    /// </summary>
    public IReadOnlyList<Sample> Query(TimeSpan range, int maxPoints)
    {
        var cutoff = DateTime.UtcNow - range;

        List<Sample> source;

        lock (_lock)
        {
            source = range <= TimeSpan.FromHours(2)
                ? _history.Where(s => s.At >= cutoff).ToList()
                : _minutes.Where(s => s.At >= cutoff).ToList();

            // A freshly started process has no live ring yet but may have minute rows
            // on disk, so fall back rather than drawing an empty chart.
            if (source.Count < 2 && range <= TimeSpan.FromHours(2))
            {
                source = _minutes.Where(s => s.At >= cutoff).ToList();
            }
        }

        return Reduce(source, maxPoints);
    }

    /// <summary>
    /// Averages neighbouring points down to a target count. Counters keep the last
    /// value in each bucket so rates derived from them stay meaningful.
    /// </summary>
    private static List<Sample> Reduce(List<Sample> source, int maxPoints)
    {
        if (source.Count <= maxPoints || maxPoints < 2)
        {
            return source;
        }

        int bucket = (int)Math.Ceiling(source.Count / (double)maxPoints);
        var output = new List<Sample>(maxPoints + 1);

        for (var i = 0; i < source.Count; i += bucket)
        {
            var slice = source.GetRange(i, Math.Min(bucket, source.Count - i));
            var last = slice[^1];

            output.Add(new Sample(
                last.At,
                Math.Round(slice.Average(s => s.CpuPercent), 2),
                (long)slice.Average(s => s.WorkingSetBytes),
                (long)slice.Average(s => s.ManagedBytes),
                (int)Math.Round(slice.Average(s => s.Threads)),
                (int)Math.Round(slice.Average(s => s.Players)),
                slice.Max(s => s.Entities),
                last.PacketsIn,
                last.PacketsOut,
                last.BytesIn,
                last.BytesOut));
        }

        return output;
    }

    public void Sample_(FusionServer server)
    {
        var now = DateTime.UtcNow;

        _process.Refresh();

        var cpuTotal = _process.TotalProcessorTime;
        double elapsed = (now - _lastSampledAt).TotalSeconds;

        double cpu = 0;

        if (elapsed > 0.1 && _lastCpu > TimeSpan.Zero)
        {
            // Normalised across cores, so 100% means every core is saturated.
            cpu = (cpuTotal - _lastCpu).TotalSeconds / (elapsed * Environment.ProcessorCount) * 100.0;
            cpu = Math.Clamp(cpu, 0, 100);
        }

        if (elapsed > 0.1)
        {
            PacketsInPerSecond = Math.Max(0, server.PacketsIn - _lastPacketsIn) / elapsed;
            PacketsOutPerSecond = Math.Max(0, server.PacketsOut - _lastPacketsOut) / elapsed;
            BytesInPerSecond = Math.Max(0, server.BytesIn - _lastBytesIn) / elapsed;
            BytesOutPerSecond = Math.Max(0, server.BytesOut - _lastBytesOut) / elapsed;
        }

        _lastCpu = cpuTotal;
        _lastSampledAt = now;
        _lastPacketsIn = server.PacketsIn;
        _lastPacketsOut = server.PacketsOut;
        _lastBytesIn = server.BytesIn;
        _lastBytesOut = server.BytesOut;

        var sample = new Sample(
            now,
            Math.Round(cpu, 2),
            _process.WorkingSet64,
            GC.GetTotalMemory(false),
            _process.Threads.Count,
            server.Players.Count,
            server.Entities.Count,
            server.PacketsIn,
            server.PacketsOut,
            server.BytesIn,
            server.BytesOut);

        Latest = sample;

        lock (_lock)
        {
            _history.Add(sample);

            if (_history.Count > MaxSamples)
            {
                _history.RemoveRange(0, _history.Count - MaxSamples);
            }
        }

        RollMinute(now);
    }

    /// <summary>Writes one durable row per wall-clock minute, averaged from the 5s samples.</summary>
    private void RollMinute(DateTime now)
    {
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

        if (_minuteBucket == DateTime.MinValue)
        {
            _minuteBucket = minute;
            return;
        }

        if (minute <= _minuteBucket)
        {
            return;
        }

        List<Sample> slice;

        lock (_lock)
        {
            slice = _history.Where(h => h.At >= _minuteBucket && h.At < minute).ToList();
        }

        if (slice.Count > 0)
        {
            var last = slice[^1];

            AppendMinute(new Sample(
                _minuteBucket,
                Math.Round(slice.Average(h => h.CpuPercent), 2),
                (long)slice.Average(h => h.WorkingSetBytes),
                (long)slice.Average(h => h.ManagedBytes),
                (int)Math.Round(slice.Average(h => h.Threads)),
                (int)Math.Round(slice.Average(h => h.Players)),
                slice.Max(h => h.Entities),
                last.PacketsIn, last.PacketsOut, last.BytesIn, last.BytesOut));
        }

        _minuteBucket = minute;
    }

    // ---- persistence ----

    /// <summary>Loads previously written minute rows so the graphs span restarts.</summary>
    public void OpenStore(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _metricsPath = Path.Combine(directory, "metrics.csv");

            if (!File.Exists(_metricsPath))
            {
                return;
            }

            lock (_lock)
            {
                foreach (string line in File.ReadLines(_metricsPath))
                {
                    if (ParseRow(line) is { } row)
                    {
                        _minutes.Add(row);
                    }
                }

                Trim();
            }
        }
        catch
        {
            _metricsPath = null;
        }
    }

    private static Sample? ParseRow(string line)
    {
        var f = line.Split(',');

        if (f.Length < 11 || !long.TryParse(f[0], out long unix))
        {
            return null;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;

        try
        {
            return new Sample(
                DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime,
                double.Parse(f[1], inv),
                long.Parse(f[2]), long.Parse(f[3]), int.Parse(f[4]), int.Parse(f[5]),
                int.Parse(f[6]), long.Parse(f[7]), long.Parse(f[8]), long.Parse(f[9]),
                long.Parse(f[10]));
        }
        catch
        {
            return null;
        }
    }

    private static string Row(Sample m)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        return $"{new DateTimeOffset(m.At, TimeSpan.Zero).ToUnixTimeSeconds()}," +
               $"{m.CpuPercent.ToString(inv)},{m.WorkingSetBytes},{m.ManagedBytes}," +
               $"{m.Threads},{m.Players},{m.Entities}," +
               $"{m.PacketsIn},{m.PacketsOut},{m.BytesIn},{m.BytesOut}";
    }

    private void AppendMinute(Sample sample)
    {
        lock (_lock)
        {
            _minutes.Add(sample);
            Trim();
        }

        if (_metricsPath == null)
        {
            return;
        }

        try
        {
            File.AppendAllText(_metricsPath, Row(sample) + Environment.NewLine);
        }
        catch
        {
            // Never let metrics writing disturb the server.
        }
    }

    /// <summary>Drops rows past the retention window, rewriting the file when it shrinks.</summary>
    private void Trim()
    {
        if (_minutes.Count <= MaxMinutes)
        {
            return;
        }

        _minutes.RemoveRange(0, _minutes.Count - MaxMinutes);

        if (_metricsPath == null)
        {
            return;
        }

        try
        {
            File.WriteAllLines(_metricsPath, _minutes.Select(Row));
        }
        catch
        {
        }
    }

    // ---- host machine ----

    public sealed record HostStats(
        double LoadAverage,
        long MemoryTotalBytes,
        long MemoryAvailableBytes,
        int ProcessorCount,
        string Platform,
        string Framework);

    /// <summary>
    /// Machine-wide numbers. Linux exposes these through /proc; anywhere else the
    /// memory figures come back as zero and the panel simply hides them.
    /// </summary>
    public HostStats ReadHost()
    {
        double load = 0;
        long total = 0, available = 0;

        try
        {
            if (File.Exists("/proc/loadavg"))
            {
                var parts = File.ReadAllText("/proc/loadavg").Split(' ');

                double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out load);
            }

            if (File.Exists("/proc/meminfo"))
            {
                foreach (string line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        total = ParseKb(line);
                    }
                    else if (line.StartsWith("MemAvailable:"))
                    {
                        available = ParseKb(line);
                        break;
                    }
                }
            }
        }
        catch
        {
            // Reporting resource use must never be able to take the server down.
        }

        return new HostStats(
            Math.Round(load, 2),
            total,
            available,
            Environment.ProcessorCount,
            Environment.OSVersion.Platform.ToString(),
            Environment.Version.ToString());
    }

    private static long ParseKb(string line)
    {
        var digits = new string(line.Where(char.IsDigit).ToArray());

        return long.TryParse(digits, out var kb) ? kb * 1024 : 0;
    }
}
