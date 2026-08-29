# Fusion Dedicated Phase 1: Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the relay an authenticated panel, container-correct resource figures, layered spawn blocklists, and a console command surface with persistent ranks — all without needing a game client or a container to verify.

**Architecture:** Every new unit is pure and Steam-free so it can be unit tested. Parsing and policy live in static helpers or small classes behind interfaces; the thin adapters that touch `FusionServer`, `HttpListener` and stdin stay untested by design and hold no logic. `CommandProcessor` talks to `ICommandTarget` rather than `FusionServer`, which is what lets the whole command surface be tested without Steamworks.

**Tech Stack:** .NET 9, C# with nullable enabled, xUnit, `System.Text.Json`, `HttpListener`, Steamworks.NET 2024.8.0 (existing, not touched by this phase).

**Spec:** `docs/superpowers/specs/2026-08-29-fusion-dedicated-headless-egg-design.md`

## Global Constraints

- Target framework is `net9.0`. Do not change it.
- `Nullable` is `enable` and `ImplicitUsings` is `enable` in the main project. Match this in the test project.
- Namespaces follow the folder: `FusionDedicated`, `FusionDedicated.Server`, `FusionDedicated.Web`, `FusionDedicated.Protocol`. The protocol files also use `BonelabServerBrowser.Fusion`; leave that alone.
- File-scoped namespaces, Allman braces, four-space indent. Match the surrounding code.
- Comments: do not explain simple functions. Two sentences maximum unless something genuinely needs more. This is a repository rule.
- `PermissionLevel` values must stay `Guest = -1, Default = 0, Operator = 1, Owner = 2`. Clients compare against these numbers.
- Entity IDs below `EntityRegistry.FirstEntityId` (256) are player rigs. Never allocate or despawn in that range.
- Resource reporting must never throw out of `ReadHost`. The existing catch-all stays.
- Nothing is pushed to a public remote. Commit locally on branch `headless-egg`.
- No test may call `SteamAPI` or construct `FusionServer`.

---

### Task 1: Test project and fixed-time credential comparison

**Files:**
- Create: `FusionDedicated.Tests/FusionDedicated.Tests.csproj`
- Create: `FusionDedicated.sln`
- Create: `FusionDedicated/Web/DashboardAuth.cs`
- Test: `FusionDedicated.Tests/Web/DashboardAuthTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class FusionDedicated.Web.DashboardAuth` with
  `public static bool ConstantTimeEquals(string? a, string? b)`.

- [ ] **Step 1: Create the solution and test project**

```bash
cd z:/Dev/BonelabFusionDedicated/fusion-dedicated
dotnet new sln -n FusionDedicated
dotnet sln add FusionDedicated/FusionDedicated.csproj
dotnet new xunit -o FusionDedicated.Tests -f net9.0
dotnet sln add FusionDedicated.Tests/FusionDedicated.Tests.csproj
dotnet add FusionDedicated.Tests/FusionDedicated.Tests.csproj reference FusionDedicated/FusionDedicated.csproj
```

- [ ] **Step 2: Write the failing test**

Create `FusionDedicated.Tests/Web/DashboardAuthTests.cs`:

```csharp
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class DashboardAuthTests
{
    [Theory]
    [InlineData("hunter2", "hunter2", true)]
    [InlineData("hunter2", "hunter3", false)]
    [InlineData("hunter2", "hunter22", false)]
    [InlineData("", "", true)]
    [InlineData(null, "hunter2", false)]
    [InlineData("hunter2", null, false)]
    [InlineData(null, null, false)]
    public void ConstantTimeEquals_compares_correctly(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, DashboardAuth.ConstantTimeEquals(a, b));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~ConstantTimeEquals"`
Expected: FAIL — the build errors with `CS0246: The type or namespace name 'DashboardAuth' could not be found`.

- [ ] **Step 4: Write minimal implementation**

Create `FusionDedicated/Web/DashboardAuth.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace FusionDedicated.Web;

public static class DashboardAuth
{
    /// <summary>
    /// Compares two secrets without leaking their contents through timing. A null on
    /// either side never matches, including null against null.
    /// </summary>
    public static bool ConstantTimeEquals(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
```

`FixedTimeEquals` returns false for differing lengths without comparing, which is
the accepted behaviour: the length of a password is not the secret.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~ConstantTimeEquals"`
Expected: PASS, 7 tests.

- [ ] **Step 6: Commit**

```bash
git add FusionDedicated.sln FusionDedicated.Tests FusionDedicated/Web/DashboardAuth.cs
git commit -m "Add test project and fixed-time credential comparison"
```

---

### Task 2: Basic auth parsing and Dashboard enforcement

**Files:**
- Modify: `FusionDedicated/Web/DashboardAuth.cs`
- Modify: `FusionDedicated/ServerConfig.cs` (add two properties near `DashboardHost`, around line 262)
- Modify: `FusionDedicated/Web/Dashboard.cs:79` (top of `Handle`)
- Test: `FusionDedicated.Tests/Web/DashboardAuthTests.cs`

**Interfaces:**
- Consumes: `DashboardAuth.ConstantTimeEquals` from Task 1.
- Produces:
  - `public static (string User, string Password)? TryParseBasic(string? header)`
  - `public static bool IsAuthorized(string? header, string user, string password)`
  - `ServerConfig.DashboardUser` (string, default `"admin"`), `ServerConfig.DashboardPassword` (string, default `""`).

- [ ] **Step 1: Write the failing tests**

Append to `FusionDedicated.Tests/Web/DashboardAuthTests.cs`, inside the class:

```csharp
    private static string Basic(string user, string password)
        => "Basic " + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));

    [Fact]
    public void TryParseBasic_reads_user_and_password()
    {
        var parsed = DashboardAuth.TryParseBasic(Basic("admin", "hunter2"));

        Assert.NotNull(parsed);
        Assert.Equal("admin", parsed!.Value.User);
        Assert.Equal("hunter2", parsed.Value.Password);
    }

    [Fact]
    public void TryParseBasic_keeps_colons_in_the_password()
    {
        var parsed = DashboardAuth.TryParseBasic(Basic("admin", "a:b:c"));

        Assert.Equal("a:b:c", parsed!.Value.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic")]
    [InlineData("Basic !!!not base64!!!")]
    [InlineData("Basic bm9jb2xvbg==")]
    public void TryParseBasic_returns_null_for_junk(string? header)
    {
        Assert.Null(DashboardAuth.TryParseBasic(header));
    }

    [Fact]
    public void IsAuthorized_accepts_correct_credentials()
    {
        Assert.True(DashboardAuth.IsAuthorized(Basic("admin", "hunter2"), "admin", "hunter2"));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("wrong", "hunter2")]
    public void IsAuthorized_rejects_wrong_credentials(string user, string password)
    {
        Assert.False(DashboardAuth.IsAuthorized(Basic(user, password), "admin", "hunter2"));
    }

    [Fact]
    public void IsAuthorized_allows_everything_when_no_password_is_set()
    {
        Assert.True(DashboardAuth.IsAuthorized(null, "admin", ""));
    }
```

The last case matters: a localhost panel with no password must keep working
exactly as it does today.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~DashboardAuthTests"`
Expected: FAIL — `CS0117: 'DashboardAuth' does not contain a definition for 'TryParseBasic'`.

- [ ] **Step 3: Implement the parsing and check**

Add to `FusionDedicated/Web/DashboardAuth.cs` inside the class:

```csharp
    private const string Scheme = "Basic ";

    /// <summary>
    /// Splits an HTTP Basic header into its two halves. Returns null for anything
    /// malformed rather than throwing, because this runs on untrusted input.
    /// </summary>
    public static (string User, string Password)? TryParseBasic(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string encoded = header[Scheme.Length..].Trim();

        if (encoded.Length == 0)
        {
            return null;
        }

        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }

        int split = decoded.IndexOf(':');

        if (split < 0)
        {
            return null;
        }

        return (decoded[..split], decoded[(split + 1)..]);
    }

    /// <summary>
    /// True when the request may proceed. An empty configured password disables the
    /// check entirely, which is what keeps a passwordless localhost panel usable.
    /// </summary>
    public static bool IsAuthorized(string? header, string user, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return true;
        }

        var parsed = TryParseBasic(header);

        if (parsed is null)
        {
            return false;
        }

        return ConstantTimeEquals(parsed.Value.User, user)
            && ConstantTimeEquals(parsed.Value.Password, password);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~DashboardAuthTests"`
Expected: PASS, all tests.

- [ ] **Step 5: Add the config keys**

In `FusionDedicated/ServerConfig.cs`, immediately after the `DashboardHost`
property (near line 262), add:

```csharp
    /// <summary>Username for the panel's HTTP Basic prompt.</summary>
    public string DashboardUser { get; set; } = "admin";

    /// <summary>
    /// Panel password. Empty disables the check, which is only safe on loopback —
    /// see the bind refusal in Dashboard.Start.
    /// </summary>
    public string DashboardPassword { get; set; } = "";
```

- [ ] **Step 6: Enforce it in the request path**

In `FusionDedicated/Web/Dashboard.cs`, at the very top of `Handle` (line 79),
before `string path = ...`:

```csharp
        if (!DashboardAuth.IsAuthorized(
                context.Request.Headers["Authorization"],
                _config.DashboardUser,
                _config.DashboardPassword))
        {
            context.Response.StatusCode = 401;
            context.Response.AddHeader("WWW-Authenticate",
                "Basic realm=\"Fusion Dedicated\"");
            context.Response.Close();
            return;
        }
```

- [ ] **Step 7: Verify the whole suite and the build**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add FusionDedicated/Web/DashboardAuth.cs FusionDedicated/Web/Dashboard.cs FusionDedicated/ServerConfig.cs FusionDedicated.Tests/Web/DashboardAuthTests.cs
git commit -m "Require a password on the control panel"
```

---

### Task 3: Refuse to publish an unauthenticated panel

**Files:**
- Modify: `FusionDedicated/Web/DashboardAuth.cs`
- Modify: `FusionDedicated/Web/Dashboard.cs:40` (`Start`)
- Test: `FusionDedicated.Tests/Web/DashboardBindPolicyTests.cs`

**Interfaces:**
- Consumes: `ServerConfig.DashboardPassword` from Task 2.
- Produces: `public static bool IsLoopback(string host)` and
  `public static string? BindRefusalReason(string host, string password)` — null means
  binding is allowed.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Web/DashboardBindPolicyTests.cs`:

```csharp
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class DashboardBindPolicyTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_hosts_are_recognised(string host)
    {
        Assert.True(DashboardAuth.IsLoopback(host));
    }

    [Theory]
    [InlineData("+")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    public void Public_hosts_are_not_loopback(string host)
    {
        Assert.False(DashboardAuth.IsLoopback(host));
    }

    [Fact]
    public void Public_host_without_a_password_is_refused()
    {
        Assert.NotNull(DashboardAuth.BindRefusalReason("+", ""));
    }

    [Fact]
    public void Public_host_with_a_password_is_allowed()
    {
        Assert.Null(DashboardAuth.BindRefusalReason("+", "hunter2"));
    }

    [Fact]
    public void Loopback_without_a_password_is_allowed()
    {
        Assert.Null(DashboardAuth.BindRefusalReason("localhost", ""));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~DashboardBindPolicyTests"`
Expected: FAIL — `CS0117: 'DashboardAuth' does not contain a definition for 'IsLoopback'`.

- [ ] **Step 3: Implement the policy**

Add to `FusionDedicated/Web/DashboardAuth.cs` inside the class:

```csharp
    public static bool IsLoopback(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1"
        || host == "::1";

    /// <summary>
    /// Why the panel must not bind, or null when it may. Publishing an admin
    /// interface with no password on a reachable address is refused rather than
    /// warned about.
    /// </summary>
    public static string? BindRefusalReason(string host, string password)
    {
        if (IsLoopback(host) || !string.IsNullOrEmpty(password))
        {
            return null;
        }

        return $"DashboardHost is '{host}' but no DashboardPassword is set. "
             + "Set a password, or bind the panel to localhost and tunnel in.";
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~DashboardBindPolicyTests"`
Expected: PASS, 11 tests.

- [ ] **Step 5: Apply it in Start**

Replace the body of `Start` in `FusionDedicated/Web/Dashboard.cs`:

```csharp
    public void Start()
    {
        var refusal = DashboardAuth.BindRefusalReason(
            _config.DashboardHost, _config.DashboardPassword);

        if (refusal != null)
        {
            _server.Log("ERROR", $"Control panel not started: {refusal}");
            return;
        }

        _listener.Prefixes.Add(Prefix);
        _listener.Start();

        _ = Task.Run(LoopAsync);
    }
```

`Program.Main` logs `dashboard.Url` after calling `Start`. Change that line so it
does not claim a panel that refused to bind — in `FusionDedicated/Program.cs`,
replace `server.Log("INFO", $"Control panel: {dashboard.Url}");` with:

```csharp
            if (dashboard.IsListening)
            {
                server.Log("INFO", $"Control panel: {dashboard.Url}");
            }
```

and add to `Dashboard`:

```csharp
    public bool IsListening => _listener.IsListening;
```

- [ ] **Step 6: Verify build and suite**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add FusionDedicated/Web/DashboardAuth.cs FusionDedicated/Web/Dashboard.cs FusionDedicated/Program.cs FusionDedicated.Tests/Web/DashboardBindPolicyTests.cs
git commit -m "Refuse to publish the panel without a password"
```

---

### Task 4: cgroup parsing helpers

**Files:**
- Create: `FusionDedicated/Server/CgroupReader.cs`
- Test: `FusionDedicated.Tests/Server/CgroupReaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class FusionDedicated.Server.CgroupReader` with
  - `public static long? ParseMemoryLimit(string? contents)`
  - `public static int? ParseCpuQuota(string? cpuMax)`
  - `public static int? ParseV1CpuQuota(string? quota, string? period)`

  All return null for "no limit set" or unparsable input. Memory is bytes, CPU is a
  whole core count rounded up with a floor of 1.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/CgroupReaderTests.cs`:

```csharp
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class CgroupReaderTests
{
    [Theory]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("  2147483648\n", 2147483648L)]
    public void ParseMemoryLimit_reads_a_byte_count(string contents, long expected)
    {
        Assert.Equal(expected, CgroupReader.ParseMemoryLimit(contents));
    }

    [Theory]
    [InlineData("max")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a number")]
    [InlineData("-1")]
    [InlineData("0")]
    public void ParseMemoryLimit_returns_null_when_unlimited_or_junk(string? contents)
    {
        Assert.Null(CgroupReader.ParseMemoryLimit(contents));
    }

    [Theory]
    [InlineData("200000 100000", 2)]
    [InlineData("100000 100000", 1)]
    [InlineData("150000 100000", 2)]
    [InlineData("50000 100000", 1)]
    public void ParseCpuQuota_divides_quota_by_period(string cpuMax, int expected)
    {
        Assert.Equal(expected, CgroupReader.ParseCpuQuota(cpuMax));
    }

    [Theory]
    [InlineData("max 100000")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("200000")]
    [InlineData("200000 0")]
    public void ParseCpuQuota_returns_null_when_unlimited_or_junk(string? cpuMax)
    {
        Assert.Null(CgroupReader.ParseCpuQuota(cpuMax));
    }

    [Theory]
    [InlineData("200000", "100000", 2)]
    [InlineData("150000", "100000", 2)]
    public void ParseV1CpuQuota_divides_quota_by_period(string quota, string period, int expected)
    {
        Assert.Equal(expected, CgroupReader.ParseV1CpuQuota(quota, period));
    }

    [Theory]
    [InlineData("-1", "100000")]
    [InlineData("200000", "0")]
    [InlineData(null, "100000")]
    public void ParseV1CpuQuota_returns_null_when_unlimited_or_junk(string? quota, string? period)
    {
        Assert.Null(CgroupReader.ParseV1CpuQuota(quota, period));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~CgroupReaderTests"`
Expected: FAIL — `CS0246: The type or namespace name 'CgroupReader' could not be found`.

- [ ] **Step 3: Implement the parsers**

Create `FusionDedicated/Server/CgroupReader.cs`:

```csharp
using System.Globalization;

namespace FusionDedicated.Server;

/// <summary>
/// Reads the container's own limits. Inside a container /proc describes the whole
/// node, so the panel would otherwise graph the host's memory rather than the
/// server's allowance.
/// </summary>
public static class CgroupReader
{
    public static long? ParseMemoryLimit(string? contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return null;
        }

        string trimmed = contents.Trim();

        if (trimmed == "max")
        {
            return null;
        }

        if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes))
        {
            return null;
        }

        return bytes > 0 ? bytes : null;
    }

    /// <summary>Parses cgroup v2 "cpu.max", which holds a quota and a period.</summary>
    public static int? ParseCpuQuota(string? cpuMax)
    {
        if (string.IsNullOrWhiteSpace(cpuMax))
        {
            return null;
        }

        var parts = cpuMax.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length != 2 ? null : ParseV1CpuQuota(parts[0], parts[1]);
    }

    public static int? ParseV1CpuQuota(string? quota, string? period)
    {
        if (string.IsNullOrWhiteSpace(quota) || string.IsNullOrWhiteSpace(period))
        {
            return null;
        }

        if (!long.TryParse(quota.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long q)
            || !long.TryParse(period.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long p))
        {
            return null;
        }

        if (q <= 0 || p <= 0)
        {
            return null;
        }

        return Math.Max(1, (int)Math.Ceiling((double)q / p));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~CgroupReaderTests"`
Expected: PASS, 22 tests.

- [ ] **Step 5: Commit**

```bash
git add FusionDedicated/Server/CgroupReader.cs FusionDedicated.Tests/Server/CgroupReaderTests.cs
git commit -m "Add cgroup limit parsing"
```

---

### Task 5: Report container limits rather than the node's

**Files:**
- Modify: `FusionDedicated/Server/CgroupReader.cs`
- Modify: `FusionDedicated/Server/ResourceMonitor.cs:350-356` (the `HostStats` record) and `ReadHost`
- Test: `FusionDedicated.Tests/Server/CgroupSelectionTests.cs`

**Interfaces:**
- Consumes: `CgroupReader.ParseMemoryLimit`, `ParseCpuQuota`, `ParseV1CpuQuota` from Task 4.
- Produces:
  - `CgroupReader.StatsSource` enum: `Host`, `CgroupV1`, `CgroupV2`.
  - `public static (long Total, long Available, int Cpus, CgroupReader.StatsSource Source)? TryReadLimits(Func<string, string?> readFile)`
  - `ResourceMonitor.HostStats` gains a trailing `string Source` parameter.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/CgroupSelectionTests.cs`:

```csharp
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class CgroupSelectionTests
{
    private static Func<string, string?> Files(Dictionary<string, string> map)
        => path => map.TryGetValue(path, out var value) ? value : null;

    [Fact]
    public void Prefers_cgroup_v2_when_present()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory.max"] = "2147483648",
            ["/sys/fs/cgroup/memory.current"] = "536870912",
            ["/sys/fs/cgroup/cpu.max"] = "200000 100000",
        });

        var limits = CgroupReader.TryReadLimits(read);

        Assert.NotNull(limits);
        Assert.Equal(2147483648L, limits!.Value.Total);
        Assert.Equal(2147483648L - 536870912L, limits.Value.Available);
        Assert.Equal(2, limits.Value.Cpus);
        Assert.Equal(CgroupReader.StatsSource.CgroupV2, limits.Value.Source);
    }

    [Fact]
    public void Falls_back_to_cgroup_v1()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory/memory.limit_in_bytes"] = "1073741824",
            ["/sys/fs/cgroup/memory/memory.usage_in_bytes"] = "268435456",
            ["/sys/fs/cgroup/cpu/cpu.cfs_quota_us"] = "400000",
            ["/sys/fs/cgroup/cpu/cpu.cfs_period_us"] = "100000",
        });

        var limits = CgroupReader.TryReadLimits(read);

        Assert.NotNull(limits);
        Assert.Equal(1073741824L, limits!.Value.Total);
        Assert.Equal(4, limits.Value.Cpus);
        Assert.Equal(CgroupReader.StatsSource.CgroupV1, limits.Value.Source);
    }

    [Fact]
    public void Returns_null_when_no_cgroup_files_exist()
    {
        Assert.Null(CgroupReader.TryReadLimits(Files(new())));
    }

    [Fact]
    public void Returns_null_when_memory_is_unlimited()
    {
        var read = Files(new() { ["/sys/fs/cgroup/memory.max"] = "max" });

        Assert.Null(CgroupReader.TryReadLimits(read));
    }

    [Fact]
    public void Missing_cpu_limit_still_reports_memory()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory.max"] = "2147483648",
            ["/sys/fs/cgroup/memory.current"] = "0",
            ["/sys/fs/cgroup/cpu.max"] = "max 100000",
        });

        var limits = CgroupReader.TryReadLimits(read);

        Assert.NotNull(limits);
        Assert.Equal(Environment.ProcessorCount, limits!.Value.Cpus);
    }

    [Fact]
    public void Usage_above_the_limit_never_reports_negative_available()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory.max"] = "1000",
            ["/sys/fs/cgroup/memory.current"] = "2000",
        });

        Assert.Equal(0, CgroupReader.TryReadLimits(read)!.Value.Available);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~CgroupSelectionTests"`
Expected: FAIL — `CS0117: 'CgroupReader' does not contain a definition for 'TryReadLimits'`.

- [ ] **Step 3: Implement selection**

Add to `FusionDedicated/Server/CgroupReader.cs` inside the class:

```csharp
    public enum StatsSource
    {
        Host,
        CgroupV1,
        CgroupV2,
    }

    /// <summary>
    /// Memory and CPU as the container sees them, or null when no limit is set. The
    /// file reader is injected so this stays testable off Linux.
    /// </summary>
    public static (long Total, long Available, int Cpus, StatsSource Source)? TryReadLimits(
        Func<string, string?> readFile)
    {
        long? v2 = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory.max"));

        if (v2 is { } totalV2)
        {
            long used = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory.current")) ?? 0;
            int cpus = ParseCpuQuota(readFile("/sys/fs/cgroup/cpu.max")) ?? Environment.ProcessorCount;

            return (totalV2, Math.Max(0, totalV2 - used), cpus, StatsSource.CgroupV2);
        }

        long? v1 = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory/memory.limit_in_bytes"));

        // cgroup v1 signals "no limit" with a sentinel near long.MaxValue rather than
        // the word max, so an implausibly large limit means unlimited.
        if (v1 is { } totalV1 && totalV1 < (1L << 53))
        {
            long used = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory/memory.usage_in_bytes")) ?? 0;
            int cpus = ParseV1CpuQuota(
                readFile("/sys/fs/cgroup/cpu/cpu.cfs_quota_us"),
                readFile("/sys/fs/cgroup/cpu/cpu.cfs_period_us")) ?? Environment.ProcessorCount;

            return (totalV1, Math.Max(0, totalV1 - used), cpus, StatsSource.CgroupV1);
        }

        return null;
    }

    /// <summary>Reads a file, or null if it is missing or unreadable.</summary>
    public static string? ReadFileOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~CgroupSelectionTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Use it in ReadHost**

In `FusionDedicated/Server/ResourceMonitor.cs`, change the record at line 350 to
add a trailing parameter:

```csharp
    public sealed record HostStats(
        double LoadAverage,
        long MemoryTotalBytes,
        long MemoryAvailableBytes,
        int ProcessorCount,
        string Platform,
        string Framework,
        string Source);
```

Then in `ReadHost`, replace the final `return new HostStats(...)` with:

```csharp
        var limits = CgroupReader.TryReadLimits(CgroupReader.ReadFileOrNull);

        if (limits is { } c)
        {
            return new HostStats(
                Math.Round(load, 2), c.Total, c.Available, c.Cpus,
                Environment.OSVersion.Platform.ToString(),
                Environment.Version.ToString(),
                c.Source.ToString());
        }

        return new HostStats(
            Math.Round(load, 2),
            total,
            available,
            Environment.ProcessorCount,
            Environment.OSVersion.Platform.ToString(),
            Environment.Version.ToString(),
            nameof(CgroupReader.StatsSource.Host));
```

The whole method already sits inside a `try`/`catch` that must stay.

- [ ] **Step 6: Verify build and suite**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, all tests pass. If the panel's JSON
shape is asserted anywhere it will surface here; it is not, so no page change is
needed for the added field.

- [ ] **Step 7: Commit**

```bash
git add FusionDedicated/Server/CgroupReader.cs FusionDedicated/Server/ResourceMonitor.cs FusionDedicated.Tests/Server/CgroupSelectionTests.cs
git commit -m "Report container memory and cpu limits"
```

---

### Task 6: Built-in blocklist and layered evaluation

**Files:**
- Create: `FusionDedicated/Server/Safety/BuiltInBlocklist.cs`
- Create: `FusionDedicated/Server/Safety/BlocklistEvaluator.cs`
- Modify: `FusionDedicated/Server/FusionServer.cs:532` (the `BlacklistedBarcodes` check in `HandleSpawnRequest`)
- Test: `FusionDedicated.Tests/Server/BlocklistEvaluatorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public static class BuiltInBlocklist` with `public static readonly IReadOnlySet<string> Barcodes`.
  - `public sealed record BlockVerdict(bool Blocked, string Layer, string Reason)`.
  - `public sealed class BlocklistEvaluator` with a constructor taking
    `IReadOnlySet<string> operatorBarcodes` and
    `public BlockVerdict Check(string barcode)`.

Task 8 extends this class with the fetched global list; keep the constructor
signature stable by adding an optional parameter there rather than reordering.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/BlocklistEvaluatorTests.cs`:

```csharp
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class BlocklistEvaluatorTests
{
    private static BlocklistEvaluator Evaluator(params string[] operatorBarcodes)
        => new(new HashSet<string>(operatorBarcodes, StringComparer.Ordinal));

    [Fact]
    public void Built_in_barcode_is_blocked()
    {
        var verdict = Evaluator().Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke");

        Assert.True(verdict.Blocked);
        Assert.Equal("built-in", verdict.Layer);
    }

    [Fact]
    public void Operator_barcode_is_blocked()
    {
        var verdict = Evaluator("Some.Mod.Spawnable.Thing").Check("Some.Mod.Spawnable.Thing");

        Assert.True(verdict.Blocked);
        Assert.Equal("operator", verdict.Layer);
    }

    [Fact]
    public void Unlisted_barcode_is_allowed()
    {
        Assert.False(Evaluator().Check("SLZ.BONELAB.Spawnable.Crate").Blocked);
    }

    [Fact]
    public void Built_in_wins_over_an_operator_list_that_omits_it()
    {
        var verdict = Evaluator("Unrelated.Thing")
            .Check("BaBaCorp.MiscExplosiveDevices.Spawnable.MicroNukeGrenade");

        Assert.True(verdict.Blocked);
        Assert.Equal("built-in", verdict.Layer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_barcode_is_allowed_rather_than_throwing(string barcode)
    {
        Assert.False(Evaluator().Check(barcode).Blocked);
    }

    [Fact]
    public void Matching_is_case_sensitive_because_barcodes_are()
    {
        Assert.False(Evaluator().Check("babacorp.miscexplosivedevices.spawnable.timednuke").Blocked);
    }

    [Fact]
    public void Built_in_list_contains_the_known_crash_payload()
    {
        Assert.Contains("SLZ.BONELAB.Core.Spawnable.GameplaySystems", BuiltInBlocklist.Barcodes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~BlocklistEvaluatorTests"`
Expected: FAIL — `CS0246: The type or namespace name 'BlocklistEvaluator' could not be found`.

- [ ] **Step 3: Write the built-in list**

Create `FusionDedicated/Server/Safety/BuiltInBlocklist.cs`:

```csharp
namespace FusionDedicated.Server.Safety;

/// <summary>
/// Grief payloads that no server should allow. Ported from BoneLabAntiNuke's
/// BarcodeMatcher.AlwaysBlocked. Configuration cannot whitelist these; adding one
/// means changing this file and shipping a build, which is deliberate.
/// </summary>
public static class BuiltInBlocklist
{
    public static readonly IReadOnlySet<string> Barcodes = new HashSet<string>(StringComparer.Ordinal)
    {
        // BaBaCorp Misc Explosive Devices (mod.io 4158753) — nukes
        "BaBaCorp.MiscExplosiveDevices.Spawnable.MicroNukeGrenade",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionMicroNuke",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionTimedNuke",

        // M72 LAW launchers and their projectile
        "BaBaCorp.MiscExplosiveDevices.Spawnable.M72LAW",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.M72LawINF",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.LAWRocket",

        // Voidnade and its suction effect
        "BaBaCorp.MiscExplosiveDevices.Spawnable.KCB4VoidTunnelingDevice",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionVoidSuction",

        // Missiles and their explosion entities
        "BaBaCorp.MiscExplosiveDevices.Spawnable.Missile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.MiniMissile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.IncinMissile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionMissile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionFlameMissile",

        // AIPClient crash payload
        "SLZ.BONELAB.Core.Spawnable.GameplaySystems",

        // Known griefer avatars
        "Rett64bit.DBDPack.Avatar.Pig",
        "cheetoboa.DL2improved.Avatar.DL2IMPDemolishermassive",
    };
}
```

- [ ] **Step 4: Write the evaluator**

Create `FusionDedicated/Server/Safety/BlocklistEvaluator.cs`:

```csharp
namespace FusionDedicated.Server.Safety;

public sealed record BlockVerdict(bool Blocked, string Layer, string Reason)
{
    public static readonly BlockVerdict Allowed = new(false, "", "");
}

/// <summary>
/// Decides whether a barcode may be spawned. Layers are checked built-in first so a
/// permissive operator list can never re-enable a known grief payload.
/// </summary>
public sealed class BlocklistEvaluator
{
    private readonly IReadOnlySet<string> _operatorBarcodes;

    public BlocklistEvaluator(IReadOnlySet<string> operatorBarcodes)
    {
        _operatorBarcodes = operatorBarcodes;
    }

    public BlockVerdict Check(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BlockVerdict.Allowed;
        }

        if (BuiltInBlocklist.Barcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "built-in", "known grief payload");
        }

        if (_operatorBarcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "operator", "on this server's blacklist");
        }

        return BlockVerdict.Allowed;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~BlocklistEvaluatorTests"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Use it in the spawn path**

In `FusionDedicated/Server/FusionServer.cs`, add a field beside the other
readonly fields near the top of the class:

```csharp
    private BlocklistEvaluator _blocklist = new(new HashSet<string>(StringComparer.Ordinal));
```

Add `using FusionDedicated.Server.Safety;` to the file's usings. Rebuild the
evaluator wherever config changes take effect — add this method to the class and
call it from `Start()` and from `PushSettings()`:

```csharp
    public void RebuildBlocklist()
    {
        _blocklist = new BlocklistEvaluator(
            new HashSet<string>(Config.BlacklistedBarcodes, StringComparer.Ordinal));
    }
```

Then replace the existing check in `HandleSpawnRequest` (line 532):

```csharp
        var verdict = _blocklist.Check(request.Value.Barcode);

        if (verdict.Blocked)
        {
            Log("WARN", $"Spawn of '{request.Value.Barcode}' by {sender.DisplayName} " +
                        $"denied by the {verdict.Layer} blocklist: {verdict.Reason}");
            return;
        }
```

The later `var verdict = Guard.Check(...)` in the same method now collides on the
name. Rename that one to `guardVerdict` and update its three uses in the lines
below it.

- [ ] **Step 7: Verify build and suite**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add FusionDedicated/Server/Safety FusionDedicated/Server/FusionServer.cs FusionDedicated.Tests/Server/BlocklistEvaluatorTests.cs
git commit -m "Add a built-in grief payload blocklist"
```

---

### Task 7: Fusion safety list models and parsing

**Files:**
- Create: `FusionDedicated/Server/Safety/SafetyLists.cs`
- Test: `FusionDedicated.Tests/Server/SafetyListParsingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public sealed class GlobalModEntry` with `Barcodes` (`List<string>`), `ModId` (`int`), `NameId` (`string`).
  - `public sealed class GlobalModBlacklist` with `Mods` (`List<GlobalModEntry>`).
  - `public sealed class GlobalBanPlatform` with `PlatformId` (`ulong`), `Platform` (`string`).
  - `public sealed class GlobalBanEntry` with `Username`, `Reason`, `Platforms` (`List<GlobalBanPlatform>`).
  - `public sealed class GlobalBanList` with `Bans` (`List<GlobalBanEntry>`).
  - `public static class SafetyListParser` with
    `public static GlobalModBlacklist? ParseModBlacklist(string json)` and
    `public static GlobalBanList? ParseBanList(string json)`, both returning null on
    malformed input.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/SafetyListParsingTests.cs`:

```csharp
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class SafetyListParsingTests
{
    private const string ModBlacklistJson = """
    {
      "mods": [
        { "barcodes": [], "modID": 4423882, "nameID": "fursonas" },
        {
          "barcodes": [
            "SLZ.BONELAB.Core.Spawnable.RigManagerBlank",
            "SLZ.BONELAB.Core.Spawnable.GameplaySystems"
          ],
          "modID": -1,
          "nameID": "bonelab"
        }
      ]
    }
    """;

    private const string BanListJson = """
    {
      "bans": [
        {
          "username": "Daytrip",
          "reason": "Malicious Client Use",
          "games": [ { "game": "BONELAB" } ],
          "platforms": [ { "platformID": 76561198889496180, "platform": "Steam" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Parses_the_mod_blacklist()
    {
        var list = SafetyListParser.ParseModBlacklist(ModBlacklistJson);

        Assert.NotNull(list);
        Assert.Equal(2, list!.Mods.Count);
        Assert.Equal(4423882, list.Mods[0].ModId);
        Assert.Equal("fursonas", list.Mods[0].NameId);
        Assert.Contains("SLZ.BONELAB.Core.Spawnable.GameplaySystems", list.Mods[1].Barcodes);
    }

    [Fact]
    public void Parses_the_ban_list()
    {
        var list = SafetyListParser.ParseBanList(BanListJson);

        Assert.NotNull(list);
        var ban = Assert.Single(list!.Bans);
        Assert.Equal("Daytrip", ban.Username);
        Assert.Equal("Malicious Client Use", ban.Reason);
        Assert.Equal(76561198889496180UL, Assert.Single(ban.Platforms).PlatformId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"mods\": ")]
    public void Malformed_mod_blacklist_returns_null(string json)
    {
        Assert.Null(SafetyListParser.ParseModBlacklist(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public void Malformed_ban_list_returns_null(string json)
    {
        Assert.Null(SafetyListParser.ParseBanList(json));
    }

    [Fact]
    public void Missing_arrays_become_empty_rather_than_null()
    {
        var list = SafetyListParser.ParseModBlacklist("{}");

        Assert.NotNull(list);
        Assert.Empty(list!.Mods);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~SafetyListParsingTests"`
Expected: FAIL — `CS0246: The type or namespace name 'SafetyListParser' could not be found`.

- [ ] **Step 3: Implement the models and parser**

Create `FusionDedicated/Server/Safety/SafetyLists.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Safety;

public sealed class GlobalModEntry
{
    [JsonPropertyName("barcodes")]
    public List<string> Barcodes { get; set; } = new();

    [JsonPropertyName("modID")]
    public int ModId { get; set; } = -1;

    [JsonPropertyName("nameID")]
    public string NameId { get; set; } = "";
}

public sealed class GlobalModBlacklist
{
    [JsonPropertyName("mods")]
    public List<GlobalModEntry> Mods { get; set; } = new();
}

public sealed class GlobalBanPlatform
{
    [JsonPropertyName("platformID")]
    public ulong PlatformId { get; set; }

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "";
}

public sealed class GlobalBanEntry
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("platforms")]
    public List<GlobalBanPlatform> Platforms { get; set; } = new();
}

public sealed class GlobalBanList
{
    [JsonPropertyName("bans")]
    public List<GlobalBanEntry> Bans { get; set; } = new();
}

/// <summary>
/// Reads the community lists Fusion publishes at
/// github.com/Lakatrazz/Fusion-Lists. Returns null rather than throwing, so a
/// corrupt download can be discarded in favour of the cache.
/// </summary>
public static class SafetyListParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static GlobalModBlacklist? ParseModBlacklist(string json) => Parse<GlobalModBlacklist>(json);

    public static GlobalBanList? ParseBanList(string json) => Parse<GlobalBanList>(json);

    private static T? Parse<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~SafetyListParsingTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add FusionDedicated/Server/Safety/SafetyLists.cs FusionDedicated.Tests/Server/SafetyListParsingTests.cs
git commit -m "Parse Fusion's global safety lists"
```

---

### Task 8: Enforce the global mod blacklist, with caching

**Files:**
- Create: `FusionDedicated/Server/Safety/SafetyListStore.cs`
- Modify: `FusionDedicated/Server/Safety/BlocklistEvaluator.cs`
- Modify: `FusionDedicated/ServerConfig.cs`
- Test: `FusionDedicated.Tests/Server/GlobalBlocklistTests.cs`

**Interfaces:**
- Consumes: `GlobalModBlacklist` and `SafetyListParser` from Task 7, `BlockVerdict` from Task 6.
- Produces:
  - `BlocklistEvaluator` constructor gains an optional third-position pair:
    `BlocklistEvaluator(IReadOnlySet<string> operatorBarcodes, GlobalModBlacklist? global = null, IReadOnlyDictionary<string, int>? catalogue = null)`.
  - `ServerConfig.GlobalListsEnabled` (bool, default `true`).
  - `public sealed class SafetyListStore` with
    `public SafetyListStore(string cacheDirectory)`,
    `public GlobalModBlacklist? Mods { get; }`, `public GlobalBanList? Bans { get; }`,
    `public Task RefreshAsync(Func<string, Task<string?>> download)`,
    `public void LoadCache()`.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/GlobalBlocklistTests.cs`:

```csharp
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class GlobalBlocklistTests
{
    private static GlobalModBlacklist Global() => new()
    {
        Mods =
        {
            new GlobalModEntry { NameId = "gun-gun", ModId = 4457523 },
            new GlobalModEntry
            {
                NameId = "bonelab",
                ModId = -1,
                Barcodes = { "SLZ.BONELAB.Core.Spawnable.RigManagerBlank" },
            },
        },
    };

    private static BlocklistEvaluator Evaluator(
        GlobalModBlacklist? global = null,
        IReadOnlyDictionary<string, int>? catalogue = null)
        => new(new HashSet<string>(StringComparer.Ordinal), global, catalogue);

    [Fact]
    public void Global_barcode_is_blocked()
    {
        var verdict = Evaluator(Global()).Check("SLZ.BONELAB.Core.Spawnable.RigManagerBlank");

        Assert.True(verdict.Blocked);
        Assert.Equal("global", verdict.Layer);
    }

    [Fact]
    public void Name_id_matches_the_pallet_portion_of_a_barcode()
    {
        var verdict = Evaluator(Global()).Check("gun-gun.SomePallet.Spawnable.Thing");

        Assert.True(verdict.Blocked);
        Assert.Equal("global", verdict.Layer);
    }

    [Fact]
    public void Mod_id_blocks_only_a_catalogued_barcode()
    {
        var catalogue = new Dictionary<string, int> { ["Author.Pallet.Spawnable.X"] = 4457523 };

        Assert.True(Evaluator(Global(), catalogue).Check("Author.Pallet.Spawnable.X").Blocked);
        Assert.False(Evaluator(Global()).Check("Author.Pallet.Spawnable.X").Blocked);
    }

    [Fact]
    public void A_null_global_list_blocks_nothing_extra()
    {
        Assert.False(Evaluator().Check("SLZ.BONELAB.Core.Spawnable.RigManagerBlank").Blocked);
    }

    [Fact]
    public void Built_in_still_wins_when_a_global_list_is_present()
    {
        var verdict = Evaluator(Global()).Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke");

        Assert.Equal("built-in", verdict.Layer);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~GlobalBlocklistTests"`
Expected: FAIL — constructor does not take three arguments.

- [ ] **Step 3: Extend the evaluator**

Replace the field, constructor and `Check` in
`FusionDedicated/Server/Safety/BlocklistEvaluator.cs`:

```csharp
    private readonly IReadOnlySet<string> _operatorBarcodes;
    private readonly GlobalModBlacklist? _global;
    private readonly IReadOnlyDictionary<string, int> _catalogue;

    public BlocklistEvaluator(
        IReadOnlySet<string> operatorBarcodes,
        GlobalModBlacklist? global = null,
        IReadOnlyDictionary<string, int>? catalogue = null)
    {
        _operatorBarcodes = operatorBarcodes;
        _global = global;
        _catalogue = catalogue ?? new Dictionary<string, int>();
    }

    public BlockVerdict Check(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BlockVerdict.Allowed;
        }

        if (BuiltInBlocklist.Barcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "built-in", "known grief payload");
        }

        if (_global != null && MatchesGlobal(barcode, out string reason))
        {
            return new BlockVerdict(true, "global", reason);
        }

        if (_operatorBarcodes.Contains(barcode))
        {
            return new BlockVerdict(true, "operator", "on this server's blacklist");
        }

        return BlockVerdict.Allowed;
    }

    /// <summary>
    /// A barcode is Author.Pallet.Type.Name, so its first segment is the name id
    /// Fusion's list uses. Mod id matching needs the learned catalogue, because a
    /// spawn request carries no mod id of its own.
    /// </summary>
    private bool MatchesGlobal(string barcode, out string reason)
    {
        string nameId = barcode.Split('.', 2)[0];
        _catalogue.TryGetValue(barcode, out int modId);

        foreach (var mod in _global!.Mods)
        {
            if (mod.Barcodes.Contains(barcode, StringComparer.Ordinal))
            {
                reason = $"barcode listed under '{mod.NameId}'";
                return true;
            }

            if (!string.IsNullOrEmpty(mod.NameId)
                && string.Equals(mod.NameId, nameId, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"mod '{mod.NameId}' is blacklisted";
                return true;
            }

            if (mod.ModId > 0 && modId == mod.ModId)
            {
                reason = $"mod.io id {mod.ModId} is blacklisted";
                return true;
            }
        }

        reason = "";
        return false;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~GlobalBlocklistTests"`
Expected: PASS, 5 tests. Re-run `BlocklistEvaluatorTests` too; the optional
parameters keep it compiling unchanged.

- [ ] **Step 5: Write the store test**

Append to `FusionDedicated.Tests/Server/GlobalBlocklistTests.cs` a second class:

```csharp
public class SafetyListStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-tests-" + Guid.NewGuid());

    public SafetyListStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Good = """{ "mods": [ { "barcodes": ["A.B.C.D"], "modID": 1, "nameID": "x" } ] }""";

    [Fact]
    public async Task Refresh_stores_a_good_download_in_the_cache()
    {
        var store = new SafetyListStore(_dir);

        await store.RefreshAsync(_ => Task.FromResult<string?>(Good));

        Assert.NotNull(store.Mods);
        Assert.True(File.Exists(Path.Combine(_dir, "globalModBlacklist.json")));
    }

    [Fact]
    public async Task A_failed_download_keeps_the_previous_cache()
    {
        var store = new SafetyListStore(_dir);
        await store.RefreshAsync(_ => Task.FromResult<string?>(Good));

        await store.RefreshAsync(_ => Task.FromResult<string?>(null));

        Assert.NotNull(store.Mods);
        Assert.Single(store.Mods!.Mods);
    }

    [Fact]
    public async Task A_malformed_download_keeps_the_previous_cache()
    {
        var store = new SafetyListStore(_dir);
        await store.RefreshAsync(_ => Task.FromResult<string?>(Good));

        await store.RefreshAsync(_ => Task.FromResult<string?>("not json"));

        Assert.Single(store.Mods!.Mods);
    }

    [Fact]
    public void LoadCache_reads_what_a_previous_run_saved()
    {
        File.WriteAllText(Path.Combine(_dir, "globalModBlacklist.json"), Good);

        var store = new SafetyListStore(_dir);
        store.LoadCache();

        Assert.Single(store.Mods!.Mods);
    }

    [Fact]
    public void LoadCache_with_no_cache_leaves_the_lists_null()
    {
        var store = new SafetyListStore(_dir);
        store.LoadCache();

        Assert.Null(store.Mods);
    }
}
```

- [ ] **Step 6: Implement the store**

Create `FusionDedicated/Server/Safety/SafetyListStore.cs`:

```csharp
namespace FusionDedicated.Server.Safety;

/// <summary>
/// Holds Fusion's community lists and keeps a copy on disk. A server with no
/// outbound internet still starts: a failed or corrupt fetch falls back to the
/// cache, and an absent cache leaves the built-in layer to do the work.
/// </summary>
public sealed class SafetyListStore
{
    public const string RepositoryUrl =
        "https://raw.githubusercontent.com/Lakatrazz/Fusion-Lists/main/";

    private const string ModsFile = "globalModBlacklist.json";
    private const string BansFile = "globalBans.json";

    private readonly string _cacheDirectory;

    public SafetyListStore(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
    }

    public GlobalModBlacklist? Mods { get; private set; }

    public GlobalBanList? Bans { get; private set; }

    public void LoadCache()
    {
        Mods = SafetyListParser.ParseModBlacklist(ReadCache(ModsFile) ?? "") ?? Mods;
        Bans = SafetyListParser.ParseBanList(ReadCache(BansFile) ?? "") ?? Bans;
    }

    /// <summary>
    /// Fetches both lists. The downloader is injected so this is testable without a
    /// network, and returns null for any failure.
    /// </summary>
    public async Task RefreshAsync(Func<string, Task<string?>> download)
    {
        string? mods = await download(RepositoryUrl + ModsFile);
        var parsedMods = mods is null ? null : SafetyListParser.ParseModBlacklist(mods);

        if (parsedMods != null)
        {
            Mods = parsedMods;
            WriteCache(ModsFile, mods!);
        }

        string? bans = await download(RepositoryUrl + BansFile);
        var parsedBans = bans is null ? null : SafetyListParser.ParseBanList(bans);

        if (parsedBans != null)
        {
            Bans = parsedBans;
            WriteCache(BansFile, bans!);
        }
    }

    private string? ReadCache(string name)
    {
        try
        {
            string path = Path.Combine(_cacheDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private void WriteCache(string name, string contents)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllText(Path.Combine(_cacheDirectory, name), contents);
        }
        catch
        {
            // A read-only volume costs a refetch next start, nothing more.
        }
    }
}
```

- [ ] **Step 7: Add the config key and wire it up**

In `FusionDedicated/ServerConfig.cs`, beside the other limit properties:

```csharp
    /// <summary>
    /// Whether to fetch and enforce Fusion's community mod blacklist. The built-in
    /// list is enforced either way.
    /// </summary>
    public bool GlobalListsEnabled { get; set; } = true;
```

In `FusionDedicated/Server/FusionServer.cs`, add a property and extend
`RebuildBlocklist` from Task 6:

```csharp
    public SafetyListStore? SafetyLists { get; set; }

    public void RebuildBlocklist()
    {
        _blocklist = new BlocklistEvaluator(
            new HashSet<string>(Config.BlacklistedBarcodes, StringComparer.Ordinal),
            Config.GlobalListsEnabled ? SafetyLists?.Mods : null,
            Config.ModCatalog
                .Where(m => m.ModId > 0)
                .GroupBy(m => m.Barcode)
                .ToDictionary(g => g.Key, g => g.First().ModId, StringComparer.Ordinal));
    }
```

In `FusionDedicated/Program.cs`, after `server.Resources.OpenStore(...)`, add:

```csharp
        var safety = new SafetyListStore(Path.Combine(AppContext.BaseDirectory, "lists"));
        safety.LoadCache();
        server.SafetyLists = safety;
        server.RebuildBlocklist();

        if (config.GlobalListsEnabled)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            await safety.RefreshAsync(async url =>
            {
                try { return await http.GetStringAsync(url); }
                catch { return null; }
            });

            server.RebuildBlocklist();
            server.Log("INFO", $"Safety lists: {safety.Mods?.Mods.Count ?? 0} blacklisted mods, " +
                               $"{safety.Bans?.Bans.Count ?? 0} global bans");
        }
```

Add `using FusionDedicated.Server.Safety;` to `Program.cs`.

- [ ] **Step 8: Verify build and suite**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, all tests pass.

- [ ] **Step 9: Commit**

```bash
git add FusionDedicated/Server/Safety FusionDedicated/Server/FusionServer.cs FusionDedicated/ServerConfig.cs FusionDedicated/Program.cs FusionDedicated.Tests/Server/GlobalBlocklistTests.cs
git commit -m "Enforce Fusion's global mod blacklist with an offline cache"
```

---

### Task 9: Flag globally banned players without kicking them

**Files:**
- Create: `FusionDedicated/Server/Safety/GlobalBanCheck.cs`
- Modify: `FusionDedicated/Server/FusionServer.cs` (in `HandleConnectionRequest`, after `player.Permission` is set around line 483)
- Test: `FusionDedicated.Tests/Server/GlobalBanCheckTests.cs`

**Interfaces:**
- Consumes: `GlobalBanList` from Task 7, `SafetyListStore` from Task 8.
- Produces: `public static class GlobalBanCheck` with
  `public static GlobalBanEntry? Find(GlobalBanList? list, ulong platformId)`.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/GlobalBanCheckTests.cs`:

```csharp
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class GlobalBanCheckTests
{
    private static GlobalBanList List() => new()
    {
        Bans =
        {
            new GlobalBanEntry
            {
                Username = "Daytrip",
                Reason = "Malicious Client Use",
                Platforms = { new GlobalBanPlatform { PlatformId = 76561198889496180, Platform = "Steam" } },
            },
        },
    };

    [Fact]
    public void Finds_a_listed_platform_id()
    {
        var found = GlobalBanCheck.Find(List(), 76561198889496180);

        Assert.NotNull(found);
        Assert.Equal("Malicious Client Use", found!.Reason);
    }

    [Fact]
    public void Returns_null_for_an_unlisted_id()
    {
        Assert.Null(GlobalBanCheck.Find(List(), 76561190000000000));
    }

    [Fact]
    public void Returns_null_for_a_null_list()
    {
        Assert.Null(GlobalBanCheck.Find(null, 76561198889496180));
    }

    [Fact]
    public void Handles_an_entry_with_no_platforms()
    {
        var list = new GlobalBanList { Bans = { new GlobalBanEntry { Username = "nobody" } } };

        Assert.Null(GlobalBanCheck.Find(list, 1));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~GlobalBanCheckTests"`
Expected: FAIL — `CS0246: The type or namespace name 'GlobalBanCheck' could not be found`.

- [ ] **Step 3: Implement the lookup**

Create `FusionDedicated/Server/Safety/GlobalBanCheck.cs`:

```csharp
namespace FusionDedicated.Server.Safety;

/// <summary>
/// Looks a joining player up in Fusion's community ban list. This never refuses a
/// join on its own: a third-party list should not decide who may play here, and a
/// false positive would lock out a friend without explanation.
/// </summary>
public static class GlobalBanCheck
{
    public static GlobalBanEntry? Find(GlobalBanList? list, ulong platformId)
    {
        if (list is null)
        {
            return null;
        }

        foreach (var ban in list.Bans)
        {
            foreach (var platform in ban.Platforms)
            {
                if (platform.PlatformId == platformId)
                {
                    return ban;
                }
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~GlobalBanCheckTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Log it at join**

In `FusionDedicated/Server/FusionServer.cs`, in `HandleConnectionRequest`,
immediately after the line `player.Metadata[PermissionMetadataKey] = ...`
(around line 483):

```csharp
        if (GlobalBanCheck.Find(SafetyLists?.Bans, platformId) is { } globalBan)
        {
            Log("WARN", $"{player.DisplayName} is on Fusion's global ban list " +
                        $"as '{globalBan.Username}': {globalBan.Reason}. " +
                        "Not enforced — ban them here if you agree.");
        }
```

- [ ] **Step 6: Verify build and suite**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add FusionDedicated/Server/Safety/GlobalBanCheck.cs FusionDedicated/Server/FusionServer.cs FusionDedicated.Tests/Server/GlobalBanCheckTests.cs
git commit -m "Warn when a joining player is on the global ban list"
```

---

### Task 10: ranks.json store with migration

**Files:**
- Create: `FusionDedicated/Server/Ranks/RankStore.cs`
- Test: `FusionDedicated.Tests/Server/RankStoreTests.cs`

**Interfaces:**
- Consumes: `PermissionLevel` and `PermissionEntry` from `FusionDedicated`.
- Produces: `public sealed class RankStore` with
  - `public RankStore(string path)`
  - `public void Load()`, `public void Save()`
  - `public PermissionLevel Get(ulong platformId)`
  - `public void Set(ulong platformId, string username, PermissionLevel level)`
  - `public IReadOnlyDictionary<ulong, RankEntry> Entries { get; }`
  - `public int MigrateFrom(IEnumerable<PermissionEntry> existing)` returning the count added
  - `public int MergeSeed(IEnumerable<ulong> ids, PermissionLevel level)` returning the count added
  - `public sealed class RankEntry { public PermissionLevel Rank; public string Name; }`

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/RankStoreTests.cs`:

```csharp
using FusionDedicated;
using FusionDedicated.Server.Ranks;

namespace FusionDedicated.Tests.Server;

public class RankStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-ranks-" + Guid.NewGuid());

    public RankStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Path_ => System.IO.Path.Combine(_dir, "ranks.json");

    [Fact]
    public void Unlisted_player_is_default()
    {
        Assert.Equal(PermissionLevel.Default, new RankStore(Path_).Get(1));
    }

    [Fact]
    public void Set_then_get_round_trips_through_disk()
    {
        var store = new RankStore(Path_);
        store.Set(76561198000000000, "spudgun", PermissionLevel.Owner);
        store.Save();

        var reloaded = new RankStore(Path_);
        reloaded.Load();

        Assert.Equal(PermissionLevel.Owner, reloaded.Get(76561198000000000));
        Assert.Equal("spudgun", reloaded.Entries[76561198000000000].Name);
    }

    [Fact]
    public void Setting_default_removes_the_entry()
    {
        var store = new RankStore(Path_);
        store.Set(1, "x", PermissionLevel.Operator);
        store.Set(1, "x", PermissionLevel.Default);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void A_malformed_file_keeps_the_previous_roster()
    {
        var store = new RankStore(Path_);
        store.Set(1, "x", PermissionLevel.Owner);
        store.Save();
        store.Load();

        File.WriteAllText(Path_, "{ not json");
        store.Load();

        Assert.Equal(PermissionLevel.Owner, store.Get(1));
    }

    [Fact]
    public void Migration_copies_entries_and_skips_duplicates()
    {
        var store = new RankStore(Path_);
        store.Set(1, "already", PermissionLevel.Owner);

        int added = store.MigrateFrom(new[]
        {
            new PermissionEntry { PlatformId = 1, Username = "already", Level = PermissionLevel.Operator },
            new PermissionEntry { PlatformId = 2, Username = "new", Level = PermissionLevel.Operator },
        });

        Assert.Equal(1, added);
        Assert.Equal(PermissionLevel.Owner, store.Get(1));
        Assert.Equal(PermissionLevel.Operator, store.Get(2));
    }

    [Fact]
    public void Seeding_adds_without_removing_existing_entries()
    {
        var store = new RankStore(Path_);
        store.Set(1, "existing", PermissionLevel.Operator);

        int added = store.MergeSeed(new ulong[] { 2, 3 }, PermissionLevel.Owner);

        Assert.Equal(2, added);
        Assert.Equal(PermissionLevel.Operator, store.Get(1));
        Assert.Equal(PermissionLevel.Owner, store.Get(2));
    }

    [Fact]
    public void Seeding_does_not_downgrade_an_existing_rank()
    {
        var store = new RankStore(Path_);
        store.Set(1, "owner", PermissionLevel.Owner);

        store.MergeSeed(new ulong[] { 1 }, PermissionLevel.Operator);

        Assert.Equal(PermissionLevel.Owner, store.Get(1));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RankStoreTests"`
Expected: FAIL — `CS0246: The type or namespace name 'RankStore' could not be found`.

- [ ] **Step 3: Implement the store**

Create `FusionDedicated/Server/Ranks/RankStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Ranks;

public sealed class RankEntry
{
    [JsonPropertyName("rank")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PermissionLevel Rank { get; set; } = PermissionLevel.Default;

    /// <summary>For the operator's reference. Never trusted for identity.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// The rank roster, kept in its own file so it can be edited over SFTP without
/// touching configuration, and so a stray comma cannot take the config with it.
/// </summary>
public sealed class RankStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private Dictionary<ulong, RankEntry> _entries = new();

    public RankStore(string path)
    {
        _path = path;
    }

    public IReadOnlyDictionary<ulong, RankEntry> Entries => _entries;

    public PermissionLevel Get(ulong platformId)
        => _entries.TryGetValue(platformId, out var entry) ? entry.Rank : PermissionLevel.Default;

    public void Set(ulong platformId, string username, PermissionLevel level)
    {
        if (level == PermissionLevel.Default)
        {
            _entries.Remove(platformId);
            return;
        }

        if (!_entries.TryGetValue(platformId, out var entry))
        {
            entry = new RankEntry();
            _entries[platformId] = entry;
        }

        entry.Rank = level;

        if (!string.IsNullOrWhiteSpace(username))
        {
            entry.Name = username;
        }
    }

    /// <summary>Reads the file. A malformed file is logged by the caller and ignored.</summary>
    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, RankEntry>>(
                File.ReadAllText(_path), Options);

            if (parsed is null)
            {
                return;
            }

            var rebuilt = new Dictionary<ulong, RankEntry>();

            foreach (var (key, value) in parsed)
            {
                if (ulong.TryParse(key, out ulong id))
                {
                    rebuilt[id] = value;
                }
            }

            _entries = rebuilt;
        }
        catch (JsonException)
        {
            // Keep whatever we already had rather than dropping every rank.
        }
    }

    public void Save()
    {
        try
        {
            var forDisk = _entries.ToDictionary(p => p.Key.ToString(), p => p.Value);

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllText(_path, JsonSerializer.Serialize(forDisk, Options));
        }
        catch
        {
            // Not fatal; ranks stay correct in memory until the next successful write.
        }
    }

    public int MigrateFrom(IEnumerable<PermissionEntry> existing)
    {
        var added = 0;

        foreach (var entry in existing)
        {
            if (_entries.ContainsKey(entry.PlatformId))
            {
                continue;
            }

            Set(entry.PlatformId, entry.Username, entry.Level);
            added++;
        }

        return added;
    }

    /// <summary>
    /// Merges an environment-supplied list. Never lowers a rank already held, so a
    /// promotion made by console or by hand survives a restart.
    /// </summary>
    public int MergeSeed(IEnumerable<ulong> ids, PermissionLevel level)
    {
        var added = 0;

        foreach (ulong id in ids)
        {
            if (Get(id) >= level)
            {
                continue;
            }

            Set(id, _entries.TryGetValue(id, out var e) ? e.Name : "", level);
            added++;
        }

        return added;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RankStoreTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add FusionDedicated/Server/Ranks FusionDedicated.Tests/Server/RankStoreTests.cs
git commit -m "Store ranks in their own file"
```

---

### Task 11: Import Fusion's permissionList.xml

**Files:**
- Create: `FusionDedicated/Server/Ranks/PermissionListImporter.cs`
- Test: `FusionDedicated.Tests/Server/PermissionListImporterTests.cs`

**Interfaces:**
- Consumes: `RankStore` and `RankEntry` from Task 10.
- Produces: `public static class PermissionListImporter` with
  `public static int Import(RankStore store, string xml)` returning the count added.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/PermissionListImporterTests.cs`:

```csharp
using FusionDedicated;
using FusionDedicated.Server.Ranks;

namespace FusionDedicated.Tests.Server;

public class PermissionListImporterTests
{
    private const string Xml = """
    <PermissionList>
      <Permission id="76561198000000000" username="spudgun" level="2" />
      <Permission id="76561198000000001" username="mate" level="1" />
    </PermissionList>
    """;

    private static RankStore Empty()
        => new(Path.Combine(Path.GetTempPath(), "fd-import-" + Guid.NewGuid(), "ranks.json"));

    [Fact]
    public void Imports_every_entry()
    {
        var store = Empty();

        int added = PermissionListImporter.Import(store, Xml);

        Assert.Equal(2, added);
        Assert.Equal(PermissionLevel.Owner, store.Get(76561198000000000));
        Assert.Equal(PermissionLevel.Operator, store.Get(76561198000000001));
        Assert.Equal("spudgun", store.Entries[76561198000000000].Name);
    }

    [Fact]
    public void Import_never_lowers_an_existing_rank()
    {
        var store = Empty();
        store.Set(76561198000000001, "mate", PermissionLevel.Owner);

        PermissionListImporter.Import(store, Xml);

        Assert.Equal(PermissionLevel.Owner, store.Get(76561198000000001));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<PermissionList>")]
    public void Malformed_xml_imports_nothing_rather_than_throwing(string xml)
    {
        Assert.Equal(0, PermissionListImporter.Import(Empty(), xml));
    }

    [Fact]
    public void Entries_with_bad_attributes_are_skipped()
    {
        const string bad = """
        <PermissionList>
          <Permission id="not-a-number" username="x" level="2" />
          <Permission username="no id" level="1" />
          <Permission id="5" username="ok" level="1" />
        </PermissionList>
        """;

        var store = Empty();

        Assert.Equal(1, PermissionListImporter.Import(store, bad));
        Assert.Equal(PermissionLevel.Operator, store.Get(5));
    }

    [Fact]
    public void Out_of_range_levels_are_clamped()
    {
        const string wild = """<PermissionList><Permission id="7" username="x" level="99" /></PermissionList>""";

        var store = Empty();
        PermissionListImporter.Import(store, wild);

        Assert.Equal(PermissionLevel.Owner, store.Get(7));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~PermissionListImporterTests"`
Expected: FAIL — `CS0246: The type or namespace name 'PermissionListImporter' could not be found`.

- [ ] **Step 3: Implement the importer**

Create `FusionDedicated/Server/Ranks/PermissionListImporter.cs`:

```csharp
using System.Xml.Linq;

namespace FusionDedicated.Server.Ranks;

/// <summary>
/// Reads the roster LabFusion writes when you host a normal lobby, so a rank list
/// built in game carries across to a dedicated server. The import is additive and
/// never lowers a rank already held here.
/// </summary>
public static class PermissionListImporter
{
    public static int Import(RankStore store, string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return 0;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return 0;
        }

        var added = 0;

        foreach (var element in document.Descendants("Permission"))
        {
            if (!ulong.TryParse(element.Attribute("id")?.Value, out ulong id)
                || !int.TryParse(element.Attribute("level")?.Value, out int rawLevel))
            {
                continue;
            }

            var level = PermissionLevels.Clamp(rawLevel);

            if (level == PermissionLevel.Default || store.Get(id) >= level)
            {
                continue;
            }

            store.Set(id, element.Attribute("username")?.Value ?? "", level);
            added++;
        }

        return added;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~PermissionListImporterTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add FusionDedicated/Server/Ranks/PermissionListImporter.cs FusionDedicated.Tests/Server/PermissionListImporterTests.cs
git commit -m "Import a Fusion permission list"
```

---

### Task 12: Command parsing against an abstract target

**Files:**
- Create: `FusionDedicated/Commands/ICommandTarget.cs`
- Create: `FusionDedicated/Commands/CommandProcessor.cs`
- Test: `FusionDedicated.Tests/Commands/CommandProcessorTests.cs`

**Interfaces:**
- Consumes: `PermissionLevel`, `PermissionLevels.Clamp`.
- Produces:
  - `public sealed record CommandPlayer(ulong PlatformId, byte SmallId, string Name, PermissionLevel Rank, int EntityCount)`
  - `public interface ICommandTarget` with
    `IReadOnlyList<CommandPlayer> Players { get; }`,
    `void SetRank(ulong platformId, string name, PermissionLevel level)`,
    `void Kick(byte smallId, string reason)`,
    `void Ban(ulong platformId, string name, string reason)`,
    `bool Unban(ulong platformId)`,
    `int Purge(byte smallId)`,
    `void SetLevel(string barcode, string title)`
  - `public sealed class CommandProcessor` with `public CommandProcessor(ICommandTarget target)` and
    `public string Execute(string line)`.

Task 13 implements `ICommandTarget` over `FusionServer`. Do not change these
signatures there.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Commands/CommandProcessorTests.cs`:

```csharp
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

        Assert.Equal((1, "being a nuisance"), _target.Kicks.Single());
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~CommandProcessorTests"`
Expected: FAIL — `CS0246: The type or namespace name 'ICommandTarget' could not be found`.

- [ ] **Step 3: Define the target interface**

Create `FusionDedicated/Commands/ICommandTarget.cs`:

```csharp
namespace FusionDedicated.Commands;

public sealed record CommandPlayer(
    ulong PlatformId,
    byte SmallId,
    string Name,
    PermissionLevel Rank,
    int EntityCount);

/// <summary>
/// What a command can do to the server. Kept abstract so the parser is testable
/// without Steam, and so both transports share one implementation.
/// </summary>
public interface ICommandTarget
{
    IReadOnlyList<CommandPlayer> Players { get; }

    void SetRank(ulong platformId, string name, PermissionLevel level);

    void Kick(byte smallId, string reason);

    void Ban(ulong platformId, string name, string reason);

    bool Unban(ulong platformId);

    int Purge(byte smallId);

    void SetLevel(string barcode, string title);
}
```

- [ ] **Step 4: Implement the processor**

Create `FusionDedicated/Commands/CommandProcessor.cs`:

```csharp
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
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~CommandProcessorTests"`
Expected: PASS, 18 tests.

- [ ] **Step 6: Commit**

```bash
git add FusionDedicated/Commands FusionDedicated.Tests/Commands
git commit -m "Add a command parser for console and rcon"
```

---

### Task 13: Wire commands to the server and read stdin

**Files:**
- Create: `FusionDedicated/Commands/ServerCommandTarget.cs`
- Create: `FusionDedicated/Commands/StdinCommands.cs`
- Modify: `FusionDedicated/Program.cs`
- Modify: `FusionDedicated/Server/FusionServer.cs` (`Ranks` property, and `Config.GetPermission` call at line 482)

**Interfaces:**
- Consumes: `CommandProcessor`, `ICommandTarget`, `CommandPlayer` from Task 12; `RankStore` from Task 10; `PermissionListImporter` from Task 11.
- Produces: `public sealed class ServerCommandTarget : ICommandTarget` and
  `public static class StdinCommands` with
  `public static void Start(CommandProcessor processor, Action<string> write, CancellationToken token)`.

- [ ] **Step 1: Give the server a rank store**

In `FusionDedicated/Server/FusionServer.cs`, add near `SafetyLists`:

```csharp
    public Ranks.RankStore? Ranks { get; set; }
```

Change line 482 so the rank store wins when present:

```csharp
        player.Permission = Ranks?.Get(platformId) ?? Config.GetPermission(platformId);
```

And in `SetPermission`, after `Config.SetPermission(platformId, username, level);`:

```csharp
        Ranks?.Set(platformId, username, level);
        Ranks?.Save();
```

- [ ] **Step 2: Implement the adapter**

Create `FusionDedicated/Commands/ServerCommandTarget.cs`:

```csharp
using FusionDedicated.Server;

namespace FusionDedicated.Commands;

/// <summary>Adapts FusionServer to what the command parser needs. Holds no logic.</summary>
public sealed class ServerCommandTarget : ICommandTarget
{
    private readonly FusionServer _server;

    public ServerCommandTarget(FusionServer server)
    {
        _server = server;
    }

    public IReadOnlyList<CommandPlayer> Players => _server.Players.Players
        .Select(p => new CommandPlayer(
            p.PlatformId,
            p.SmallId,
            p.DisplayName,
            p.Permission,
            _server.Entities.Entities.Count(e => e.OwnerSmallId == p.SmallId)))
        .ToList();

    public void SetRank(ulong platformId, string name, PermissionLevel level)
        => _server.SetPermission(platformId, name, level);

    public void Kick(byte smallId, string reason) => _server.Kick(smallId, reason);

    public void Ban(ulong platformId, string name, string reason)
        => _server.Ban(platformId, name, reason);

    public bool Unban(ulong platformId) => _server.Unban(platformId);

    public int Purge(byte smallId) => _server.PurgeEntitiesOf(smallId);

    public void SetLevel(string barcode, string title)
        => _server.SetLevel(barcode, title, -1, null);
}
```

- [ ] **Step 3: Implement the stdin reader**

Create `FusionDedicated/Commands/StdinCommands.cs`:

```csharp
namespace FusionDedicated.Commands;

/// <summary>
/// Reads commands from standard input. Pterodactyl pipes its console straight to
/// the process, so this is the panel's command surface.
/// </summary>
public static class StdinCommands
{
    public static void Start(CommandProcessor processor, Action<string> write, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await Console.In.ReadLineAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    return;
                }

                if (line is null)
                {
                    return;
                }

                try
                {
                    string reply = processor.Execute(line);

                    if (!string.IsNullOrEmpty(reply))
                    {
                        write(reply);
                    }
                }
                catch (Exception ex)
                {
                    write($"Command failed: {ex.Message}");
                }
            }
        }, token);
    }
}
```

A `null` line means stdin closed, which happens when the server runs without a
console attached. Returning ends the loop rather than spinning.

- [ ] **Step 4: Wire it up in Program**

In `FusionDedicated/Program.cs`, add `using FusionDedicated.Commands;` and
`using FusionDedicated.Server.Ranks;`. After the safety list block from Task 8
and before the dashboard is created, add:

```csharp
        var ranks = new RankStore(Path.Combine(AppContext.BaseDirectory, "ranks.json"));
        ranks.Load();

        int migrated = ranks.MigrateFrom(config.Permissions);

        string permissionListPath = Path.Combine(AppContext.BaseDirectory, "permissionList.xml");

        if (File.Exists(permissionListPath))
        {
            int imported = PermissionListImporter.Import(ranks, File.ReadAllText(permissionListPath));

            if (imported > 0)
            {
                server.Log("INFO", $"Imported {imported} ranks from permissionList.xml");
            }
        }

        int seededOwners = ranks.MergeSeed(ParseIds(Environment.GetEnvironmentVariable("OWNER_STEAMIDS")), PermissionLevel.Owner);
        int seededOperators = ranks.MergeSeed(ParseIds(Environment.GetEnvironmentVariable("OPERATOR_STEAMIDS")), PermissionLevel.Operator);

        if (migrated + seededOwners + seededOperators > 0)
        {
            ranks.Save();
        }

        server.Ranks = ranks;
        server.Log("INFO", $"Ranks: {ranks.Entries.Count} players listed");

        var commands = new CommandProcessor(new ServerCommandTarget(server));
```

Add this helper to the `Program` class:

```csharp
    private static IEnumerable<ulong> ParseIds(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
        {
            yield break;
        }

        foreach (string part in commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ulong.TryParse(part.Trim(), out ulong id))
            {
                yield return id;
            }
        }
    }
```

Then start the reader just after the `quit` cancellation source is created:

```csharp
        StdinCommands.Start(commands, line => Console.WriteLine(line), quit.Token);
```

- [ ] **Step 5: Verify build and the whole suite**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, every test passes.

- [ ] **Step 6: Verify by hand that commands respond**

The server cannot start without Steam, so check the parser end to end instead:

```bash
dotnet run --project FusionDedicated 2>&1 | head -20
```

Expected: the banner prints, then `Steam unavailable: SteamAPI.Init() returned false.`
and exit code 2. This confirms the new startup code compiles and runs in order
without throwing before Steam is reached. Full command verification happens in
Phase 2 once the container runs.

- [ ] **Step 7: Commit**

```bash
git add FusionDedicated/Commands FusionDedicated/Program.cs FusionDedicated/Server/FusionServer.cs
git commit -m "Read admin commands from the console"
```

---

### Task 14: Reload ranks.json when it is edited

**Files:**
- Create: `FusionDedicated/Server/Ranks/RankFileWatcher.cs`
- Modify: `FusionDedicated/Program.cs`
- Test: `FusionDedicated.Tests/Server/RankReloadTests.cs`

**Interfaces:**
- Consumes: `RankStore` from Task 10.
- Produces: `public sealed class RankFileWatcher : IDisposable` with
  `public RankFileWatcher(RankStore store, string path, Action<string> log)` and
  `public void Start()`.
  `RankStore` gains `public DateTime LastWriteSeen { get; private set; }` and
  `public bool ReloadIfChanged()` returning true when a reload happened.

The spec requires a hand edit over SFTP to apply without a restart. Polling the
file's timestamp is used rather than `FileSystemWatcher`, because change
notifications are unreliable across the bind mounts a container volume uses.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/RankReloadTests.cs`:

```csharp
using FusionDedicated;
using FusionDedicated.Server.Ranks;

namespace FusionDedicated.Tests.Server;

public class RankReloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-reload-" + Guid.NewGuid());

    public RankReloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_ => Path.Combine(_dir, "ranks.json");

    [Fact]
    public void ReloadIfChanged_is_false_when_nothing_changed()
    {
        var store = new RankStore(File_);
        store.Set(1, "x", PermissionLevel.Owner);
        store.Save();
        store.ReloadIfChanged();

        Assert.False(store.ReloadIfChanged());
    }

    [Fact]
    public void ReloadIfChanged_picks_up_an_external_edit()
    {
        var store = new RankStore(File_);
        store.Set(1, "x", PermissionLevel.Operator);
        store.Save();
        store.ReloadIfChanged();

        System.IO.File.WriteAllText(File_,
            """{ "1": { "rank": "Owner", "name": "x" }, "2": { "rank": "Operator", "name": "y" } }""");
        System.IO.File.SetLastWriteTimeUtc(File_, DateTime.UtcNow.AddSeconds(5));

        Assert.True(store.ReloadIfChanged());
        Assert.Equal(PermissionLevel.Owner, store.Get(1));
        Assert.Equal(PermissionLevel.Operator, store.Get(2));
    }

    [Fact]
    public void A_malformed_external_edit_keeps_the_previous_roster()
    {
        var store = new RankStore(File_);
        store.Set(1, "x", PermissionLevel.Owner);
        store.Save();
        store.ReloadIfChanged();

        System.IO.File.WriteAllText(File_, "{ not json");
        System.IO.File.SetLastWriteTimeUtc(File_, DateTime.UtcNow.AddSeconds(5));

        store.ReloadIfChanged();

        Assert.Equal(PermissionLevel.Owner, store.Get(1));
    }

    [Fact]
    public void ReloadIfChanged_is_false_when_the_file_does_not_exist()
    {
        Assert.False(new RankStore(File_).ReloadIfChanged());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RankReloadTests"`
Expected: FAIL — `CS1061: 'RankStore' does not contain a definition for 'ReloadIfChanged'`.

- [ ] **Step 3: Add change detection to the store**

Add to `FusionDedicated/Server/Ranks/RankStore.cs` inside the class:

```csharp
    public DateTime LastWriteSeen { get; private set; }

    /// <summary>
    /// Rereads the file when its timestamp has moved. Returns whether a reload
    /// happened, so the caller can log it.
    /// </summary>
    public bool ReloadIfChanged()
    {
        DateTime stamp;

        try
        {
            if (!File.Exists(_path))
            {
                return false;
            }

            stamp = File.GetLastWriteTimeUtc(_path);
        }
        catch
        {
            return false;
        }

        if (stamp == LastWriteSeen)
        {
            return false;
        }

        LastWriteSeen = stamp;
        Load();

        return true;
    }
```

Set the stamp when writing too, so the server's own `Save` does not read back as
an external edit. At the end of the `try` block in `Save`, after `File.WriteAllText`:

```csharp
            LastWriteSeen = File.GetLastWriteTimeUtc(_path);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RankReloadTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Poll it from a watcher**

Create `FusionDedicated/Server/Ranks/RankFileWatcher.cs`:

```csharp
namespace FusionDedicated.Server.Ranks;

/// <summary>
/// Polls ranks.json so an edit made over SFTP applies without a restart. Polling
/// beats file change notifications here, which are unreliable across the bind
/// mounts a container volume uses.
/// </summary>
public sealed class RankFileWatcher : IDisposable
{
    private readonly RankStore _store;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();

    public RankFileWatcher(RankStore store, Action<string> log)
    {
        _store = store;
        _log = log;
    }

    public void Start()
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    if (_store.ReloadIfChanged())
                    {
                        _log($"Reloaded ranks.json — {_store.Entries.Count} players listed");
                    }
                }
                catch (Exception ex)
                {
                    _log($"Could not reload ranks.json: {ex.Message}");
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
```

- [ ] **Step 6: Start it in Program**

In `FusionDedicated/Program.cs`, immediately after `server.Ranks = ranks;` from
Task 13:

```csharp
        ranks.ReloadIfChanged();

        using var rankWatcher = new RankFileWatcher(ranks, message => server.Log("INFO", message));
        rankWatcher.Start();
```

The `ReloadIfChanged` call records the current timestamp so the first poll does
not report the file as freshly edited.

- [ ] **Step 7: Verify build and the whole suite**

Run: `dotnet build -c Release && dotnet test FusionDedicated.Tests`
Expected: build succeeds with 0 warnings, every test passes.

- [ ] **Step 8: Commit**

```bash
git add FusionDedicated/Server/Ranks FusionDedicated/Program.cs FusionDedicated.Tests/Server/RankReloadTests.cs
git commit -m "Reload the ranks file when it is edited"
```

---

## Deliberately deferred to later phases

These appear in the spec's section F command table but are **not** part of Phase 1,
so their absence from `CommandProcessor` is correct rather than an oversight:

- `mute` and `unmute` — need voice packet dropping, which is G2 in Phase 3
- the `duration` argument to `ban` — needs ban expiry, which is G2 in Phase 3
- `say` — specified but unimplemented at every phase, because inspecting
  `LabFusion.dll` found no text-chat message type
- RCON as a second transport — F2 in Phase 2, which is why `CommandProcessor`
  takes a line and returns a string rather than writing to the console itself

## Phase 1 exit criteria

Before starting Phase 2, all of these must hold:

- `dotnet build -c Release` succeeds with 0 warnings
- `dotnet test FusionDedicated.Tests` passes with no skipped tests
- The panel returns 401 without credentials and 200 with them
- The panel refuses to bind when `DashboardHost` is `+` and no password is set
- `ranks.json` is written on first start and survives a restart
- Starting with no network still starts, logging that the safety lists fell back
