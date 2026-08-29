# Fusion Dedicated Phase 2: Release, RCON and the Container

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Get the relay running on BadgerPanel as a Pterodactyl egg, with a published binary, a second command transport over RCON, and a runtime image that carries Steam rather than the game.

**Architecture:** The command parser from Phase 1 stays untouched; RCON is a second transport feeding the same `CommandProcessor`. Protocol framing is a pure codec so it unit tests without a socket, and the listener gets its own integration test over loopback. The image carries system dependencies only — .NET runtime, Xvfb, the Steam client — while the server binary and every piece of mutable state live in the volume.

**Tech Stack:** .NET 9, xUnit, Source RCON protocol over TCP, Debian 12 (bookworm-slim), Xvfb, the Steam client, GitHub Actions, Pterodactyl egg format PTDL_v2.

**Spec:** `docs/superpowers/specs/2026-08-29-fusion-dedicated-headless-egg-design.md`

**Prerequisite (Phase 1):** merged to `main`. `CommandProcessor`, `ICommandTarget`,
`ServerCommandTarget`, `RankStore` and `SafetyListStore` all exist and are tested.

## Global Constraints

Everything from the Phase 1 plan still applies. Additionally:

- The image lives in `z:\Dev\BadgerPanelYolks` at `games/fusion/`, following that
  repository's conventions: `container` user at `/home/container`, `tini -g` as
  `ENTRYPOINT`, `STOPSIGNAL SIGINT`, `COPY --chown=container:container ./entrypoint.sh /entrypoint.sh`,
  `CMD ["/entrypoint.sh"]`, and the entrypoint ending in `eval ${MODIFIED_STARTUP}`.
- The egg is `egg-bonelab-fusion-headless.json` in `z:\Dev\BonelabFusionDedicated`,
  beside the existing Proton egg. Do not modify that existing egg.
- **Steam Datagram Relay needs no inbound ports for gameplay.** The only listeners
  are the panel and, when enabled, RCON.
- RCON stays disabled unless `RconPassword` is set, and refuses to listen with an
  empty password. Password comparison uses `DashboardAuth.ConstantTimeEquals`.
- `libsteam_api.so` is never committed. It is fetched at install time from the
  Steamworks.NET release that matches the `PackageReference` version, currently
  `2024.8.0`.
- No test may open a socket on a fixed port. Bind port 0 and read back the assigned
  port, so a developer machine with something already listening does not fail.

## Known environment constraint

**The Docker daemon is not running on this machine** (`docker --version` works;
`docker info` cannot reach `dockerDesktopLinuxEngine`). Tasks 1 to 5 and the
authoring of tasks 6 to 8 do not need it. Task 9 does. Start Docker Desktop before
Task 9, or run that task on the BadgerPanel node instead.

---

### Task 1: Survive a missing Steamworks native library

**Files:**
- Create: `FusionDedicated/Server/SteamStartup.cs`
- Modify: `FusionDedicated/Program.cs` (the `SteamAPI.Init()` guard)
- Test: `FusionDedicated.Tests/Server/SteamStartupTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public enum SteamInitResult { Ok, RefusedByClient, NativeLibraryMissing }`
  - `public static class SteamStartup` with
    `public static SteamInitResult TryInit(Func<bool> init)` and
    `public static string Explain(SteamInitResult result)`.

**Why:** Phase 1 verification found that `SteamAPI.Init()` **throws**
`DllNotFoundException` when the native library is absent rather than returning
false. The existing `if (!SteamAPI.Init())` guard therefore never fires, and a
container missing `libsteam_api.so` would die with an unhandled exception and a
stack trace instead of the message the code intends. In a container that is the
single most likely first-run failure, so it needs to say what is wrong.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/SteamStartupTests.cs`:

```csharp
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class SteamStartupTests
{
    [Fact]
    public void A_successful_init_is_ok()
    {
        Assert.Equal(SteamInitResult.Ok, SteamStartup.TryInit(() => true));
    }

    [Fact]
    public void A_false_return_means_the_client_refused()
    {
        Assert.Equal(SteamInitResult.RefusedByClient, SteamStartup.TryInit(() => false));
    }

    [Fact]
    public void A_missing_native_library_is_reported_rather_than_thrown()
    {
        var result = SteamStartup.TryInit(() => throw new DllNotFoundException("steam_api64"));

        Assert.Equal(SteamInitResult.NativeLibraryMissing, result);
    }

    [Fact]
    public void A_bad_image_format_also_means_the_library_is_unusable()
    {
        var result = SteamStartup.TryInit(() => throw new BadImageFormatException("wrong arch"));

        Assert.Equal(SteamInitResult.NativeLibraryMissing, result);
    }

    [Fact]
    public void An_unrelated_exception_is_not_swallowed()
    {
        Assert.Throws<InvalidOperationException>(
            () => SteamStartup.TryInit(() => throw new InvalidOperationException("something else")));
    }

    [Theory]
    [InlineData(SteamInitResult.RefusedByClient)]
    [InlineData(SteamInitResult.NativeLibraryMissing)]
    public void Every_failure_explains_itself(SteamInitResult result)
    {
        Assert.NotEmpty(SteamStartup.Explain(result));
    }

    [Fact]
    public void The_missing_library_message_names_the_file_to_supply()
    {
        Assert.Contains("libsteam_api.so", SteamStartup.Explain(SteamInitResult.NativeLibraryMissing));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~SteamStartupTests"`
Expected: FAIL — `SteamStartup` does not exist.

- [ ] **Step 3: Implement the guard**

Create `FusionDedicated/Server/SteamStartup.cs`:

```csharp
namespace FusionDedicated.Server;

public enum SteamInitResult
{
    Ok,
    RefusedByClient,
    NativeLibraryMissing,
}

/// <summary>
/// Wraps SteamAPI.Init so a missing native library is a message rather than a
/// stack trace. It throws when the library is absent instead of returning false,
/// which is the most likely first-run failure in a fresh container.
/// </summary>
public static class SteamStartup
{
    public static SteamInitResult TryInit(Func<bool> init)
    {
        try
        {
            return init() ? SteamInitResult.Ok : SteamInitResult.RefusedByClient;
        }
        catch (DllNotFoundException)
        {
            return SteamInitResult.NativeLibraryMissing;
        }
        catch (BadImageFormatException)
        {
            return SteamInitResult.NativeLibraryMissing;
        }
    }

    public static string Explain(SteamInitResult result) => result switch
    {
        SteamInitResult.NativeLibraryMissing =>
            "Steamworks could not load. libsteam_api.so is missing from the server "
            + "directory, or is built for the wrong architecture. Reinstall the "
            + "server, which fetches it from the Steamworks.NET release.",

        SteamInitResult.RefusedByClient =>
            "Steam refused to initialise. Check that the Steam client is running and "
            + "signed in, that the account owns SteamVR (app 250820), and that "
            + "steam_appid.txt sits next to the binary.",

        _ => "",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~SteamStartupTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Use it in Program**

In `FusionDedicated/Program.cs`, replace the existing guard:

```csharp
        var steam = SteamStartup.TryInit(SteamAPI.Init);

        if (steam != SteamInitResult.Ok)
        {
            Console.WriteLine(SteamStartup.Explain(steam));
            return 2;
        }
```

- [ ] **Step 6: Verify it actually reports rather than crashes**

Run: `cd FusionDedicated && dotnet run -c Release`
Expected: the banner prints, then the "libsteam_api.so is missing" message, and the
process exits **2**. Before this task the same command produced an unhandled
`DllNotFoundException`. Confirm the exit code with `echo $?`.

- [ ] **Step 7: Commit**

```bash
git add FusionDedicated/Server/SteamStartup.cs FusionDedicated/Program.cs FusionDedicated.Tests/Server/SteamStartupTests.cs
git commit -m "Explain a missing Steamworks library instead of crashing"
```

---

### Task 2: Source RCON packet codec

**Files:**
- Create: `FusionDedicated/Commands/Rcon/RconPacket.cs`
- Test: `FusionDedicated.Tests/Commands/RconPacketTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public enum RconPacketType { Response = 0, ExecCommandOrAuthResponse = 2, Auth = 3 }`
  - `public readonly record struct RconPacket(int Id, int Type, string Body)`
  - `public static class RconCodec` with
    `public static byte[] Encode(RconPacket packet)`,
    `public static int RequiredLength(ReadOnlySpan<byte> buffer)` returning -1 when a
    complete packet is not yet buffered, and
    `public static RconPacket Decode(ReadOnlySpan<byte> packet)`.

**Protocol reference.** Every packet is little-endian:
`int32 size | int32 id | int32 type | body bytes | 0x00 | 0x00`.
`size` counts everything after itself, so `size = 10 + body.Length`. Type 3 is
`SERVERDATA_AUTH`, type 2 is both `SERVERDATA_EXECCOMMAND` (inbound) and
`SERVERDATA_AUTH_RESPONSE` (outbound), type 0 is `SERVERDATA_RESPONSE_VALUE`.
A failed authentication is signalled by replying with id `-1`.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Commands/RconPacketTests.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;
using FusionDedicated.Commands.Rcon;

namespace FusionDedicated.Tests.Commands;

public class RconPacketTests
{
    [Fact]
    public void Encode_lays_out_size_id_type_body_and_two_nulls()
    {
        var bytes = RconCodec.Encode(new RconPacket(7, 2, "hi"));

        Assert.Equal(4 + 10 + 2, bytes.Length);
        Assert.Equal(12, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal("hi", Encoding.UTF8.GetString(bytes.AsSpan(12, 2)));
        Assert.Equal(0, bytes[^1]);
        Assert.Equal(0, bytes[^2]);
    }

    [Fact]
    public void Encode_then_decode_round_trips()
    {
        var original = new RconPacket(42, 3, "hunter2");

        var decoded = RconCodec.Decode(RconCodec.Encode(original));

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void An_empty_body_round_trips()
    {
        Assert.Equal("", RconCodec.Decode(RconCodec.Encode(new RconPacket(1, 0, ""))).Body);
    }

    [Fact]
    public void RequiredLength_reports_the_whole_packet_size()
    {
        var bytes = RconCodec.Encode(new RconPacket(1, 2, "abc"));

        Assert.Equal(bytes.Length, RconCodec.RequiredLength(bytes));
    }

    [Fact]
    public void RequiredLength_is_negative_until_the_size_field_arrives()
    {
        Assert.Equal(-1, RconCodec.RequiredLength(new byte[3]));
    }

    [Fact]
    public void RequiredLength_still_reports_the_size_from_a_partial_packet()
    {
        var bytes = RconCodec.Encode(new RconPacket(1, 2, "a longer body here"));

        Assert.Equal(bytes.Length, RconCodec.RequiredLength(bytes.AsSpan(0, 6)));
    }

    [Fact]
    public void An_absurd_size_is_rejected_rather_than_allocating()
    {
        var hostile = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(hostile, int.MaxValue);

        Assert.Throws<InvalidDataException>(() => RconCodec.RequiredLength(hostile));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void A_size_below_the_minimum_is_rejected(int size)
    {
        var hostile = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(hostile, size);

        Assert.Throws<InvalidDataException>(() => RconCodec.RequiredLength(hostile));
    }

    [Fact]
    public void Decode_rejects_a_truncated_packet()
    {
        Assert.Throws<InvalidDataException>(() => RconCodec.Decode(new byte[8]));
    }

    [Fact]
    public void Utf8_bodies_survive_the_round_trip()
    {
        var decoded = RconCodec.Decode(RconCodec.Encode(new RconPacket(1, 2, "café — ok")));

        Assert.Equal("café — ok", decoded.Body);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RconPacketTests"`
Expected: FAIL — `RconCodec` does not exist.

- [ ] **Step 3: Implement the codec**

Create `FusionDedicated/Commands/Rcon/RconPacket.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;

namespace FusionDedicated.Commands.Rcon;

public static class RconPacketType
{
    public const int Response = 0;

    /// <summary>Inbound this means EXECCOMMAND; outbound it means AUTH_RESPONSE.</summary>
    public const int ExecCommandOrAuthResponse = 2;

    public const int Auth = 3;
}

public readonly record struct RconPacket(int Id, int Type, string Body);

/// <summary>
/// Source RCON framing: little-endian size, id and type, then a null-terminated
/// body and one more null byte. Size counts everything after itself.
/// </summary>
public static class RconCodec
{
    /// <summary>Id, type and the two terminating nulls.</summary>
    private const int Overhead = 10;

    /// <summary>Valve's documented ceiling, with room for the size field.</summary>
    public const int MaxPacketSize = 4096;

    public static byte[] Encode(RconPacket packet)
    {
        var body = Encoding.UTF8.GetBytes(packet.Body);
        var buffer = new byte[4 + Overhead + body.Length];

        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), Overhead + body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), packet.Id);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), packet.Type);

        body.CopyTo(buffer.AsSpan(12));

        return buffer;
    }

    /// <summary>
    /// Total bytes this packet occupies, or -1 when the size field has not arrived.
    /// Throws for a size that is impossible or hostile, so a malicious client cannot
    /// make the server allocate two gigabytes.
    /// </summary>
    public static int RequiredLength(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4)
        {
            return -1;
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(buffer);

        if (size < Overhead || size > MaxPacketSize)
        {
            throw new InvalidDataException($"RCON packet size {size} is out of range.");
        }

        return size + 4;
    }

    public static RconPacket Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 4 + Overhead)
        {
            throw new InvalidDataException("RCON packet is too short.");
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(packet);
        int id = BinaryPrimitives.ReadInt32LittleEndian(packet[4..]);
        int type = BinaryPrimitives.ReadInt32LittleEndian(packet[8..]);

        int bodyLength = size - Overhead;

        if (bodyLength < 0 || 12 + bodyLength > packet.Length)
        {
            throw new InvalidDataException("RCON packet body runs past the buffer.");
        }

        return new RconPacket(id, type, Encoding.UTF8.GetString(packet.Slice(12, bodyLength)));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RconPacketTests"`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add FusionDedicated/Commands/Rcon FusionDedicated.Tests/Commands/RconPacketTests.cs
git commit -m "Add Source RCON packet framing"
```

---

### Task 3: RCON listener

**Files:**
- Create: `FusionDedicated/Commands/Rcon/RconServer.cs`
- Test: `FusionDedicated.Tests/Commands/RconServerTests.cs`

**Interfaces:**
- Consumes: `RconCodec`, `RconPacket`, `RconPacketType` from Task 2;
  `CommandProcessor` and `ICommandTarget` from Phase 1;
  `DashboardAuth.ConstantTimeEquals` from Phase 1.
- Produces: `public sealed class RconServer : IDisposable` with
  `public RconServer(CommandProcessor processor, string password, int port, Action<string, string> log)`,
  `public void Start()`, `public int Port { get; }`, `public void Stop()`.

`Port` reports the actually-bound port, which is what lets a test pass 0 and still
connect. Passing 0 in production would bind an arbitrary port, so the egg always
supplies a real one.

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Commands/RconServerTests.cs`:

```csharp
using System.Net.Sockets;
using FusionDedicated.Commands;
using FusionDedicated.Commands.Rcon;

namespace FusionDedicated.Tests.Commands;

public class RconServerTests : IDisposable
{
    private readonly RconServer _server;
    private readonly FakeTarget _target = new();

    public RconServerTests()
    {
        _server = new RconServer(new CommandProcessor(_target), "hunter2", 0, (_, _) => { });
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    private static async Task SendAsync(NetworkStream stream, RconPacket packet)
    {
        var bytes = RconCodec.Encode(packet);
        await stream.WriteAsync(bytes);
    }

    private static async Task<RconPacket> ReceiveAsync(NetworkStream stream)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header);

        int total = RconCodec.RequiredLength(header);
        var rest = new byte[total - 4];
        await stream.ReadExactlyAsync(rest);

        var whole = new byte[total];
        header.CopyTo(whole, 0);
        rest.CopyTo(whole, 4);

        return RconCodec.Decode(whole);
    }

    private async Task<NetworkStream> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", _server.Port);
        return client.GetStream();
    }

    [Fact]
    public async Task A_correct_password_authenticates_and_echoes_the_id()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(99, RconPacketType.Auth, "hunter2"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(99, reply.Id);
        Assert.Equal(RconPacketType.ExecCommandOrAuthResponse, reply.Type);
    }

    [Fact]
    public async Task A_wrong_password_replies_with_minus_one()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(99, RconPacketType.Auth, "wrong"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(-1, reply.Id);
    }

    [Fact]
    public async Task A_command_before_authentication_is_refused()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(1, RconPacketType.ExecCommandOrAuthResponse, "players"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(-1, reply.Id);
        Assert.Empty(_target.Ranks);
    }

    [Fact]
    public async Task An_authenticated_command_runs_and_returns_its_output()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(1, RconPacketType.Auth, "hunter2"));
        await ReceiveAsync(stream);

        _target.Roster.Add(new CommandPlayer(76561198000000000, 1, "Spudgun", PermissionLevel.Default, 0));

        await SendAsync(stream, new RconPacket(2, RconPacketType.ExecCommandOrAuthResponse, "players"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(2, reply.Id);
        Assert.Contains("Spudgun", reply.Body);
    }

    [Fact]
    public async Task Promote_over_rcon_reaches_the_target()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(1, RconPacketType.Auth, "hunter2"));
        await ReceiveAsync(stream);

        await SendAsync(stream, new RconPacket(2, RconPacketType.ExecCommandOrAuthResponse,
            "promote 76561198000000000 owner"));
        await ReceiveAsync(stream);

        Assert.Equal(PermissionLevel.Owner, _target.Ranks.Single().Level);
    }

    [Fact]
    public async Task Two_clients_are_served_independently()
    {
        await using var first = await ConnectAsync();
        await using var second = await ConnectAsync();

        await SendAsync(first, new RconPacket(1, RconPacketType.Auth, "hunter2"));
        await SendAsync(second, new RconPacket(2, RconPacketType.Auth, "wrong"));

        Assert.Equal(1, (await ReceiveAsync(first)).Id);
        Assert.Equal(-1, (await ReceiveAsync(second)).Id);
    }

    [Fact]
    public void An_empty_password_refuses_to_listen()
    {
        using var refused = new RconServer(new CommandProcessor(_target), "", 0, (_, _) => { });

        refused.Start();

        Assert.Equal(0, refused.Port);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RconServerTests"`
Expected: FAIL — `RconServer` does not exist.

- [ ] **Step 3: Implement the listener**

Create `FusionDedicated/Commands/Rcon/RconServer.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using FusionDedicated.Web;

namespace FusionDedicated.Commands.Rcon;

/// <summary>
/// Source RCON over TCP, so rcon-cli, BadgerPanel and Discord bots can drive the
/// same commands the console does. Refuses to listen without a password, because
/// an unauthenticated RCON port is a remote shell over the server.
/// </summary>
public sealed class RconServer : IDisposable
{
    private readonly CommandProcessor _processor;
    private readonly string _password;
    private readonly int _requestedPort;
    private readonly Action<string, string> _log;
    private readonly CancellationTokenSource _stop = new();

    private TcpListener? _listener;

    public RconServer(CommandProcessor processor, string password, int port, Action<string, string> log)
    {
        _processor = processor;
        _password = password;
        _requestedPort = port;
        _log = log;
    }

    /// <summary>The bound port, or 0 when RCON is not listening.</summary>
    public int Port { get; private set; }

    public void Start()
    {
        if (string.IsNullOrEmpty(_password))
        {
            _log("INFO", "RCON is off: no password is set.");
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, _requestedPort);
            _listener.Start();

            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }
        catch (Exception ex)
        {
            _log("ERROR", $"RCON could not listen on port {_requestedPort}: {ex.Message}");
            return;
        }

        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested && _listener != null)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var authenticated = false;

            try
            {
                await using var stream = client.GetStream();

                while (!_stop.IsCancellationRequested)
                {
                    var packet = await ReadPacketAsync(stream);

                    if (packet is not { } request)
                    {
                        return;
                    }

                    if (request.Type == RconPacketType.Auth)
                    {
                        authenticated = DashboardAuth.ConstantTimeEquals(request.Body, _password);

                        await WriteAsync(stream, new RconPacket(
                            authenticated ? request.Id : -1,
                            RconPacketType.ExecCommandOrAuthResponse,
                            ""));

                        if (!authenticated)
                        {
                            _log("WARN", "RCON authentication failed; closing the connection.");
                            return;
                        }

                        continue;
                    }

                    if (!authenticated)
                    {
                        await WriteAsync(stream, new RconPacket(-1, RconPacketType.Response, ""));
                        return;
                    }

                    string reply = _processor.Execute(request.Body);

                    _log("INFO", $"RCON: {request.Body}");

                    await WriteAsync(stream, new RconPacket(
                        request.Id, RconPacketType.Response, reply));
                }
            }
            catch (Exception ex)
            {
                _log("WARN", $"RCON connection ended: {ex.Message}");
            }
        }
    }

    private static async Task<RconPacket?> ReadPacketAsync(NetworkStream stream)
    {
        var header = new byte[4];

        try
        {
            await stream.ReadExactlyAsync(header);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        int total = RconCodec.RequiredLength(header);
        var whole = new byte[total];

        header.CopyTo(whole, 0);
        await stream.ReadExactlyAsync(whole.AsMemory(4, total - 4));

        return RconCodec.Decode(whole);
    }

    private static async Task WriteAsync(NetworkStream stream, RconPacket packet)
        => await stream.WriteAsync(RconCodec.Encode(packet));

    public void Stop()
    {
        _stop.Cancel();

        try { _listener?.Stop(); } catch { }

        Port = 0;
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }
}
```

A reply longer than `MaxPacketSize` would break framing. `CommandProcessor` output
is short, and `players` on a 255-slot server stays well inside 4 KB, so no chunking
is implemented. If a future command produces long output it must chunk.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RconServerTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add FusionDedicated/Commands/Rcon FusionDedicated.Tests/Commands/RconServerTests.cs
git commit -m "Serve admin commands over RCON"
```

---

### Task 4: Wire RCON into the server

**Files:**
- Modify: `FusionDedicated/ServerConfig.cs`
- Modify: `FusionDedicated/Program.cs`
- Test: `FusionDedicated.Tests/Server/RconConfigTests.cs`

**Interfaces:**
- Consumes: `RconServer` from Task 3.
- Produces: `ServerConfig.RconPort` (int, default `27015`) and
  `ServerConfig.RconPassword` (string, default `""`).

- [ ] **Step 1: Write the failing test**

Create `FusionDedicated.Tests/Server/RconConfigTests.cs`:

```csharp
using System.Text.Json;
using FusionDedicated;

namespace FusionDedicated.Tests.Server;

public class RconConfigTests
{
    [Fact]
    public void Rcon_is_off_by_default()
    {
        Assert.Equal("", new ServerConfig().RconPassword);
    }

    [Fact]
    public void The_default_rcon_port_is_the_source_convention()
    {
        Assert.Equal(27015, new ServerConfig().RconPort);
    }

    [Fact]
    public void Rcon_settings_survive_a_json_round_trip()
    {
        var config = new ServerConfig { RconPort = 28015, RconPassword = "hunter2" };

        var json = JsonSerializer.Serialize(config);
        var back = JsonSerializer.Deserialize<ServerConfig>(json)!;

        Assert.Equal(28015, back.RconPort);
        Assert.Equal("hunter2", back.RconPassword);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RconConfigTests"`
Expected: FAIL — `ServerConfig` has no `RconPassword`.

- [ ] **Step 3: Add the config keys**

In `FusionDedicated/ServerConfig.cs`, after `DashboardPassword`:

```csharp
    /// <summary>Port for Source RCON. Only listened on when a password is set.</summary>
    public int RconPort { get; set; } = 27015;

    /// <summary>
    /// RCON password. Empty turns RCON off entirely, which is the default: an
    /// unauthenticated RCON port is a remote shell over the server.
    /// </summary>
    public string RconPassword { get; set; } = "";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test FusionDedicated.Tests --filter "FullyQualifiedName~RconConfigTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Start it in Program**

In `FusionDedicated/Program.cs`, add `using FusionDedicated.Commands.Rcon;` and,
immediately after the `StdinCommands.Start(...)` line:

```csharp
        using var rcon = new RconServer(
            commands, config.RconPassword, config.RconPort, server.Log);

        rcon.Start();

        if (rcon.Port != 0)
        {
            server.Log("INFO", $"RCON listening on port {rcon.Port}");
        }
```

`server.Log` already matches the `Action<string, string>` the constructor expects.

- [ ] **Step 6: Verify build and the whole suite**

Run: `dotnet build -c Release && dotnet test`
Expected: build succeeds with 0 warnings, every test passes.

- [ ] **Step 7: Commit**

```bash
git add FusionDedicated/ServerConfig.cs FusionDedicated/Program.cs FusionDedicated.Tests/Server/RconConfigTests.cs
git commit -m "Start rcon when a password is configured"
```

---

### Task 5: Release workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Produces: a release asset named `fusion-dedicated-linux-x64.tar.gz` and a
  `SHA256SUMS` file, both attached to the tag's release. The install script in
  Task 7 depends on these exact names.

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/release.yml`:

```yaml
name: release

on:
  push:
    tags: [ 'v*' ]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Test
        run: dotnet test --configuration Release

      - name: Publish
        run: >
          dotnet publish FusionDedicated/FusionDedicated.csproj
          --configuration Release
          --runtime linux-x64
          --self-contained false
          --output publish

      - name: Package
        run: |
          tar -czf fusion-dedicated-linux-x64.tar.gz -C publish .
          sha256sum fusion-dedicated-linux-x64.tar.gz > SHA256SUMS
          cat SHA256SUMS

      - name: Release
        if: startsWith(github.ref, 'refs/tags/')
        uses: softprops/action-gh-release@v2
        with:
          files: |
            fusion-dedicated-linux-x64.tar.gz
            SHA256SUMS
```

`libsteam_api.so` is deliberately absent from the archive. It is Valve's
redistributable and is fetched at install time instead.

- [ ] **Step 2: Verify the packaging steps locally**

The workflow cannot run here, so run what it runs:

```bash
dotnet publish FusionDedicated/FusionDedicated.csproj -c Release -r linux-x64 --self-contained false -o publish
ls publish/fusiondedicated.dll publish/Web/index.html publish/steam_appid.txt
tar -czf /tmp/fusion-dedicated-linux-x64.tar.gz -C publish .
tar -tzf /tmp/fusion-dedicated-linux-x64.tar.gz | head
```

Expected: all three files exist in `publish/`, and the archive lists them. If
`Web/index.html` or `steam_appid.txt` are missing the panel and Steam init will
both fail in the container, so this check matters more than it looks.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Publish a linux-x64 release on tag"
```

---

### Task 6: Runtime image

**Files:**
- Create: `z:\Dev\BadgerPanelYolks\games\fusion\Dockerfile`

**Interfaces:**
- Produces: an image providing `dotnet` 9 runtime, `Xvfb`, the Steam client, `jq`,
  `curl` and `tini`, running as `container` in `/home/container`.

- [ ] **Step 1: Write the Dockerfile**

Create `z:\Dev\BadgerPanelYolks\games\fusion\Dockerfile`:

```dockerfile
# ---------------------------------------------
# BONELAB Fusion headless relay
#
# Carries Steam and a virtual display, not the game. The relay reimplements the
# Fusion wire format, so no BONELAB files are ever downloaded.
# ---------------------------------------------
FROM        --platform=$TARGETOS/$TARGETARCH debian:bookworm-slim

LABEL       org.opencontainers.image.description="Headless BONELAB Fusion relay server for Pterodactyl"

ENV         DEBIAN_FRONTEND=noninteractive

RUN         printf 'Acquire::Retries "5";\nAcquire::http::Timeout "60";\n' \
                > /etc/apt/apt.conf.d/99-badger-retries

# i386 is required: the Steam client bootstrap is still 32-bit.
RUN         dpkg --add-architecture i386 \
            && apt-get update \
            && apt-get install -y --no-install-recommends \
                ca-certificates curl wget jq tini xvfb \
                iproute2 net-tools tzdata locales \
                libgcc-s1 lib32gcc-s1 libc6 libc6:i386 libstdc++6 libstdc++6:i386 \
                libnss-wrapper gettext-base \
            && apt-get clean \
            && rm -rf /var/lib/apt/lists/*

# .NET 9 runtime only. The server is published framework-dependent, so no SDK.
RUN         wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
            && chmod +x /tmp/dotnet-install.sh \
            && /tmp/dotnet-install.sh --channel 9.0 --runtime dotnet --install-dir /usr/share/dotnet \
            && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
            && rm /tmp/dotnet-install.sh

# Steam client bootstrap. Its runtime unpacks into the volume on first start,
# which is why the egg asks for a few GB of disk.
RUN         mkdir -p /opt/steam \
            && curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steam.deb -o /tmp/steam.deb \
            && dpkg -x /tmp/steam.deb /opt/steam \
            && rm /tmp/steam.deb \
            && ln -s /opt/steam/usr/bin/steam /usr/bin/steam

ENV         NSS_WRAPPER_PASSWD=/tmp/passwd NSS_WRAPPER_GROUP=/etc/group

RUN         useradd -m -d /home/container -s /bin/bash container
USER        container
ENV         USER=container HOME=/home/container
WORKDIR     /home/container

STOPSIGNAL  SIGINT

COPY        --chown=container:container ./entrypoint.sh /entrypoint.sh
RUN         chmod +x /entrypoint.sh
ENTRYPOINT  ["/usr/bin/tini", "-g", "--"]
CMD         ["/entrypoint.sh"]
```

- [ ] **Step 2: Build it**

**Needs the Docker daemon.** Start Docker Desktop first; `docker info` must
succeed.

```bash
cd z:/Dev/BadgerPanelYolks/games/fusion
docker build -t fusion-dedicated:test .
```

Expected: build succeeds. If the Steam `.deb` URL has moved, find the current
bootstrap package rather than pinning a mirror.

- [ ] **Step 3: Verify the tools are present and runnable**

```bash
docker run --rm fusion-dedicated:test dotnet --list-runtimes
docker run --rm fusion-dedicated:test sh -c "which Xvfb steam jq tini && echo all present"
docker images fusion-dedicated:test --format "{{.Size}}"
```

Expected: a `Microsoft.NETCore.App 9.x` line, all four tools found, and a size
worth recording in the commit message so later changes can be compared against it.

- [ ] **Step 4: Commit in the yolks repository**

```bash
cd z:/Dev/BadgerPanelYolks
git add games/fusion/Dockerfile
git commit -m "Add fusion dedicated image"
```

---

### Task 7: Entrypoint and install script

**Files:**
- Create: `z:\Dev\BadgerPanelYolks\games\fusion\entrypoint.sh`

**Interfaces:**
- Consumes: the release asset names from Task 5, `ServerConfig` keys from Phase 1
  and Task 4.
- Produces: an entrypoint that starts Xvfb, signs Steam in, merges environment
  variables into `server.json`, and then runs `eval ${MODIFIED_STARTUP}`.

- [ ] **Step 1: Write the entrypoint**

Create `z:\Dev\BadgerPanelYolks\games\fusion\entrypoint.sh`:

```bash
#!/bin/bash
cd /home/container || exit 1

INTERNAL_IP=$(ip route get 1 | awk '{print $(NF-2);exit}')
export INTERNAL_IP

export DISPLAY=:99
export LD_LIBRARY_PATH=/home/container:${LD_LIBRARY_PATH}

# ---- virtual display ----
# The Steam client refuses to start without one, even headless.
rm -f /tmp/.X99-lock /tmp/.X11-unix/X99
Xvfb :99 -screen 0 1024x768x24 >/dev/null 2>&1 &

for _ in $(seq 1 15); do
    [ -e /tmp/.X11-unix/X99 ] && break
    sleep 1
done

if [ ! -e /tmp/.X11-unix/X99 ]; then
    echo "Xvfb did not start; Steam cannot run without a display."
    exit 1
fi

# ---- steam ----
# SteamAPI.Init needs a signed-in client, so this must come up before the server.
if [ -z "${STEAM_USER}" ] || [ -z "${STEAM_PASS}" ]; then
    echo "STEAM_USER and STEAM_PASS are required."
    echo "Use a separate account with SteamVR (app 250820, free) and Steam Guard off."
    exit 1
fi

if ! pgrep -x steam >/dev/null; then
    echo "Signing in to Steam as ${STEAM_USER}..."
    steam -login "${STEAM_USER}" "${STEAM_PASS}" -no-browser >/home/container/steam.log 2>&1 &
fi

for _ in $(seq 1 120); do
    pgrep -x steam >/dev/null && break
    sleep 1
done

if ! pgrep -x steam >/dev/null; then
    echo "Steam did not start within two minutes. Last lines of steam.log:"
    tail -20 /home/container/steam.log 2>/dev/null
    exit 1
fi

echo "Steam is running. Waiting for it to settle..."
sleep 15

# ---- configuration ----
# Environment variables own their own keys; everything else in server.json is
# left exactly as it was, so bans and the mod catalogue survive a restart.
if [ ! -f /home/container/server.json ]; then
    cp /home/container/server.example.json /home/container/server.json
fi

jq \
  --arg name        "${SERVER_NAME:-My Fusion Server}" \
  --arg description "${SERVER_DESCRIPTION:-}" \
  --arg barcode     "${LEVEL_BARCODE:-fa534c5a868247138f50c62e424c4144.Level.VoidG114}" \
  --arg title       "${LEVEL_TITLE:-15 - Void G114}" \
  --arg panelUser   "${PANEL_USER:-admin}" \
  --arg panelPass   "${PANEL_PASS:-}" \
  --arg rconPass    "${RCON_PASSWORD:-}" \
  --argjson players  "${MAX_PLAYERS:-10}" \
  --argjson privacy  "${PRIVACY:-0}" \
  --argjson major    "${FUSION_VERSION_MAJOR:-1}" \
  --argjson minor    "${FUSION_VERSION_MINOR:-14}" \
  --argjson entities "${MAX_ENTITIES:-2000}" \
  --argjson global   "${GLOBAL_LISTS:-true}" \
  --argjson antispam "${ANTISPAM:-true}" \
  --argjson burst    "${SPAWN_BURST_LIMIT:-25}" \
  --argjson perPlayer "${MAX_ENTITIES_PER_PLAYER:-300}" \
  --argjson panelPort "${SERVER_PORT:-8778}" \
  --argjson rconPort  "${RCON_PORT:-27015}" \
  '.ServerName = $name
   | .Description = $description
   | .MaxPlayers = $players
   | .Privacy = $privacy
   | .VersionMajor = $major
   | .VersionMinor = $minor
   | .LevelBarcode = $barcode
   | .LevelTitle = $title
   | .MaxEntities = $entities
   | .GlobalListsEnabled = $global
   | .AntiSpamEnabled = $antispam
   | .SpawnBurstLimit = $burst
   | .MaxEntitiesPerPlayer = $perPlayer
   | .DashboardHost = "+"
   | .DashboardPort = $panelPort
   | .DashboardUser = $panelUser
   | .DashboardPassword = $panelPass
   | .RconPort = $rconPort
   | .RconPassword = $rconPass' \
  /home/container/server.json > /home/container/server.json.tmp \
  && mv /home/container/server.json.tmp /home/container/server.json

if [ -z "${PANEL_PASS}" ]; then
    echo "PANEL_PASS is empty, so the control panel will refuse to start."
    echo "Set it in the panel's startup variables to enable the web interface."
fi

# ---- run ----
MODIFIED_STARTUP=$(echo -e ${STARTUP} | sed -e 's/{{/${/g' -e 's/}}/}/g')
echo -e ":/home/container$ ${MODIFIED_STARTUP}"

eval ${MODIFIED_STARTUP}
```

`DashboardHost` is forced to `+` because inside a container the panel must bind
every interface for Pterodactyl to proxy it. That is precisely the configuration
Phase 1 made refuse to start without a password, which is why `PANEL_PASS` is
checked and warned about here.

- [ ] **Step 2: Check the script parses and the jq filter is valid**

```bash
bash -n z:/Dev/BadgerPanelYolks/games/fusion/entrypoint.sh && echo "syntax ok"
```

Then prove the merge preserves state, using the real example config:

```bash
cd /tmp && cp z:/Dev/BonelabFusionDedicated/fusion-dedicated/FusionDedicated/server.example.json t.json
jq '.Bans = [{"PlatformId":123,"Username":"someone"}] | .ModCatalog = [{"Barcode":"a.b.c"}]' t.json > t2.json
SERVER_NAME="Merged" MAX_PLAYERS=24 PANEL_PASS=x jq \
  --arg name "Merged" --argjson players 24 \
  '.ServerName = $name | .MaxPlayers = $players' t2.json > t3.json
jq '{name: .ServerName, players: .MaxPlayers, bans: (.Bans | length), mods: (.ModCatalog | length)}' t3.json
```

Expected: `{"name":"Merged","players":24,"bans":1,"mods":1}` — the environment
keys changed and the accumulated state survived.

- [ ] **Step 3: Commit in the yolks repository**

```bash
cd z:/Dev/BadgerPanelYolks
git add games/fusion/entrypoint.sh
git commit -m "Add fusion dedicated entrypoint"
```

---

### Task 8: The Pterodactyl egg

**Files:**
- Create: `z:\Dev\BonelabFusionDedicated\egg-bonelab-fusion-headless.json`

**Interfaces:**
- Consumes: the image from Task 6, the entrypoint variables from Task 7, and the
  release asset names from Task 5.

- [ ] **Step 1: Write the egg**

Create `z:\Dev\BonelabFusionDedicated\egg-bonelab-fusion-headless.json` with
`meta.version` `PTDL_v2`, the image from Task 6, this startup command:

```
dotnet fusiondedicated.dll server.json
```

a `config.startup` done string of `Lobby published`, `config.stop` of `^C`, and
this installation script running in `ghcr.io/parkervcp/installers:debian`:

```bash
#!/bin/bash
apt-get update
apt-get install -y curl unzip jq ca-certificates

SERVER_DIR=/mnt/server
mkdir -p ${SERVER_DIR}
cd ${SERVER_DIR}

RELEASE_REPO="${RELEASE_REPO:-spudgun1001/fusion-dedicated}"
VERSION="${FUSION_BUILD:-latest}"

if [ "${VERSION}" == "latest" ]; then
    BASE="https://github.com/${RELEASE_REPO}/releases/latest/download"
else
    BASE="https://github.com/${RELEASE_REPO}/releases/download/${VERSION}"
fi

echo "Downloading the server from ${BASE}..."
curl -sSL -o server.tar.gz "${BASE}/fusion-dedicated-linux-x64.tar.gz"
curl -sSL -o SHA256SUMS "${BASE}/SHA256SUMS"

if ! sha256sum -c SHA256SUMS; then
    echo "ERROR: the download did not match its checksum."
    exit 1
fi

tar -xzf server.tar.gz -C ${SERVER_DIR}
rm server.tar.gz

# Valve's redistributable is not ours to bundle. Steamworks.NET publishes it, at
# the same version the server references.
echo "Fetching libsteam_api.so..."
curl -sSL -o /tmp/sw.zip \
  "https://github.com/rlabrecque/Steamworks.NET/releases/download/2024.8.0/Steamworks.NET-Standalone_2024.8.0.zip"
unzip -j -o /tmp/sw.zip "OSX-Linux-x64/libsteam_api.so" -d ${SERVER_DIR}
rm /tmp/sw.zip

if [ ! -f ${SERVER_DIR}/libsteam_api.so ]; then
    echo "ERROR: libsteam_api.so was not extracted; the server cannot start without it."
    exit 1
fi

[ -f ${SERVER_DIR}/server.json ] || cp ${SERVER_DIR}/server.example.json ${SERVER_DIR}/server.json

echo "-----------------------------------------------"
echo "Fusion Dedicated install complete"
echo "Remember: the Steam account needs SteamVR (free) and Steam Guard disabled."
echo "It does NOT need to own BONELAB."
echo "-----------------------------------------------"
```

Variables, in this order, with `STEAM_PASS`, `PANEL_PASS` and `RCON_PASSWORD`
marked `"user_viewable": false`:

| env_variable | default | rules |
|---|---|---|
| `STEAM_USER` | empty | `required|string` |
| `STEAM_PASS` | empty | `required|string` |
| `SERVER_NAME` | `My Fusion Server` | `required|string|max:64` |
| `SERVER_DESCRIPTION` | empty | `nullable|string|max:256` |
| `MAX_PLAYERS` | `10` | `required|integer|between:1,255` |
| `PRIVACY` | `0` | `required|integer|between:0,3` |
| `FUSION_VERSION_MAJOR` | `1` | `required|integer` |
| `FUSION_VERSION_MINOR` | `14` | `required|integer` |
| `LEVEL_BARCODE` | `fa534c5a868247138f50c62e424c4144.Level.VoidG114` | `required|string` |
| `LEVEL_TITLE` | `15 - Void G114` | `required|string` |
| `MAX_ENTITIES` | `2000` | `required|integer|between:100,20000` |
| `GLOBAL_LISTS` | `true` | `required|boolean` |
| `ANTISPAM` | `true` | `required|boolean` |
| `SPAWN_BURST_LIMIT` | `25` | `required|integer|between:1,1000` |
| `MAX_ENTITIES_PER_PLAYER` | `300` | `required|integer|between:10,5000` |
| `PANEL_USER` | `admin` | `required|string` |
| `PANEL_PASS` | empty | `nullable|string` |
| `RCON_PORT` | `27015` | `required|integer` |
| `RCON_PASSWORD` | empty | `nullable|string` |
| `OWNER_STEAMIDS` | empty | `nullable|string` |
| `OPERATOR_STEAMIDS` | empty | `nullable|string` |
| `FUSION_BUILD` | `latest` | `required|string` |

The description must state four things, because each one surprises somebody:

1. The Steam account needs **SteamVR (free)**, not BONELAB.
2. Steam allows **one session per account**, so this needs its own account.
3. **Steam Guard must be off** on that account, or the container cannot sign in.
4. **No inbound ports are needed for gameplay** — traffic goes over Steam's relay.
   The allocation is only for the panel, plus RCON if you enable it.
5. `OWNER_STEAMIDS` is how you make yourself Owner without touching the console.

- [ ] **Step 2: Validate the JSON**

```bash
jq -e '.meta.version == "PTDL_v2"' z:/Dev/BonelabFusionDedicated/egg-bonelab-fusion-headless.json
jq -r '.variables[].env_variable' z:/Dev/BonelabFusionDedicated/egg-bonelab-fusion-headless.json
jq -r '.scripts.installation.script' z:/Dev/BonelabFusionDedicated/egg-bonelab-fusion-headless.json | bash -n && echo "install script syntax ok"
```

Expected: `true`, all 22 variables listed, and the install script parsing cleanly.

- [ ] **Step 3: Commit**

```bash
cd z:/Dev/BonelabFusionDedicated
git add egg-bonelab-fusion-headless.json
git commit -m "Add headless fusion egg"
```

Note this directory is not currently a git repository. If it still is not,
initialise one first rather than leaving the egg untracked.

---

### Task 9: Deploy and verify on BadgerPanel

**Files:** none — this task produces evidence, not code.

**Needs:** the Docker daemon for a local smoke test, a Steam account with SteamVR
and Steam Guard disabled, and a BadgerPanel node.

- [ ] **Step 1: Smoke test the image locally**

```bash
docker run --rm -it \
  -e STEAM_USER=... -e STEAM_PASS=... \
  -e PANEL_PASS=test -e STARTUP='dotnet fusiondedicated.dll server.json' \
  -p 8778:8778 fusion-dedicated:test
```

Expected in order: Xvfb starts, Steam signs in, the banner prints,
`Relay socket listening as SteamID ...`, `Lobby published: ...`, and
`Control panel: http://<this-machine-ip>:8778/`.

- [ ] **Step 2: Import the egg and create a server**

Import `egg-bonelab-fusion-headless.json` into BadgerPanel, create a server with
at least 3 GB of disk, fill in the Steam credentials and `PANEL_PASS`, and install.

- [ ] **Step 3: Confirm each exit criterion, recording the evidence**

- `Lobby published` appears in the console
- the server is visible in BONELAB's in-game browser under `SERVER_NAME`
- the panel prompts for credentials and rejects a wrong password
- typing `players` in the BadgerPanel console replies
- typing `promote <your steamid> owner` replies, and `ranks.json` gains the entry
- with `RCON_PASSWORD` set, `rcon -a <ip>:<port> -p <pass> players` replies
- restarting the server preserves `ranks.json` and any bans
- the panel's memory graph shows the **server's** limit, not the node's

- [ ] **Step 4: Record what failed**

Any criterion that fails becomes a fix in this phase rather than a note for later.
Report the console output rather than a summary of it.

---

## Deferred to Phase 3 and 4

- `mute`, `unmute`, ban durations and the whitelist (G2)
- the moderation audit log (G4)
- client-created entity tracking (C)
- the eight message-type gates (G3)
- `say`, which stays unimplemented until a text channel is confirmed to exist

## Phase 2 exit criteria

- `dotnet build -c Release` succeeds with 0 warnings and `dotnet test` passes
- a missing `libsteam_api.so` produces a message and exit code 2, not a stack trace
- `rcon-cli` can authenticate and run `players` against a running server
- an empty `RCON_PASSWORD` leaves nothing listening on the RCON port
- the image builds and reports a .NET 9 runtime, Xvfb, Steam and jq
- a server created from the egg reaches `Lobby published` on a BadgerPanel node
- `server.json` keeps its bans and mod catalogue across a restart
