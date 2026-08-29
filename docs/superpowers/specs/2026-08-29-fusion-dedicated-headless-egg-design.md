# Fusion Dedicated: fixed limits and a Pterodactyl egg

Date: 2026-08-29
Status: approved, ready for planning
Branch: `headless-egg`

## Context

`fusion-dedicated` is a headless relay server for BONELAB Fusion. It does not run
the game. It reimplements the LabFusion wire format, publishes a Steam lobby that
the in-game browser can find, and relays packets between connected clients over
Steam Datagram Relay. It builds clean on .NET 9 and carries no stub code.

The goal is to run it on BadgerPanel as a Pterodactyl egg, and to close the gaps
that make it unsafe or misleading to run there.

## Decisions taken

These were settled before design and are not open for revisiting during
implementation.

| Question | Decision |
|---|---|
| Steam account model | One account per server. The egg takes `STEAM_USER` and `STEAM_PASS` as startup variables. |
| Steam Guard | Disabled on the server account. The account is a throwaway with no purchases, so login is unattended. |
| Validation | The operator owns BONELAB and can connect a real Fusion client, so live testing is available. |
| Config authority | Split by key. Environment variables own identity and infrastructure keys. `server.json` keeps accumulated state. |
| Image strategy | Custom runtime image in `BadgerPanelYolks`, thin install script. |
| Anti-grief delivery | Port the rules natively into the relay. No plugin system and no DLL loading. |
| Promotion channel | One command parser behind two transports: stdin and Source RCON. Alongside the existing web panel. |
| Rank storage | A separate hand-editable `ranks.json`, watched and reloaded, seeded by egg variables. |
| Moderation scope | All four capability groups: blocklist, player controls, message gates, audit log. |

## Triage of the upstream "known limits"

Two of the five documented limits are not defects and get no work:

- **Entity IDs are a `ushort`.** This is Fusion's wire format. The existing
  handling despawns a culled entity on clients before its ID is reused, which is
  already correct.
- **Player IDs 0-255 are reserved.** A protocol constraint, correctly handled by
  `EntityRegistry.FirstEntityId = 256`.

One is out of scope:

- **Gamemodes are not implemented.** This needs message tags that appear nowhere
  in the current protocol layer, and BadgerPanel does not need it. Presenting as
  plain sandbox stays the documented behaviour.

Two are real and are addressed below: untracked client-created entities, and the
missing panel authentication.

## Scope

In scope:

- **A** — dashboard authentication, plus a test project the repo currently lacks
- **B** — container-aware resource reporting
- **E** — a release workflow producing a `linux-x64` tarball
- **D** — runtime image, entrypoint, and the egg
- **F** — a console command surface on stdin
- **G** — moderation and anti-grief, in four parts
- **C** — client-created entity tracking

Out of scope: gamemodes, the two non-defects above, any change to the Proton-based
egg that already exists in `BonelabFusionDedicated`, and any plugin or DLL loading
system (see the reasoning in section G).

## Repository layout

| Repository | Contents |
|---|---|
| `spudgun1001/fusion-dedicated` (branch `headless-egg`) | A, B, C, E and tests |
| `BadgerPanelYolks` | `games/fusion/Dockerfile` and `games/fusion/entrypoint.sh` |
| `BonelabFusionDedicated` | `egg-bonelab-fusion-headless.json` |

Nothing is pushed to a public remote without explicit approval.

---

## A. Dashboard authentication

### Problem

`Dashboard.Handle` dispatches every request through one switch with no
authentication anywhere in `FusionDedicated/Web/`. Anyone who can reach the port
can ban players, wipe the world, change the map and restart the process. Setting
`DashboardHost` to `+` currently publishes that interface on every interface with
no warning.

### Design

HTTP Basic authentication, verified in `Dashboard.Handle` before the switch.
Basic rather than a login page and session cookie because browsers implement the
challenge natively and there is no session state to keep.

New keys in `ServerConfig`:

- `DashboardUser` (default `admin`)
- `DashboardPassword` (default empty)

Both belong to the environment-owned half of the config split.

Behaviour:

1. A request without valid credentials receives `401` with a
   `WWW-Authenticate: Basic realm="Fusion Dedicated"` header.
2. Credentials are compared in fixed time, so a wrong password cannot be
   recovered by measuring response times.
3. **Startup refusal.** If `DashboardHost` is not `localhost` or `127.0.0.1` and
   `DashboardPassword` is empty, the dashboard does not bind. It logs the reason
   and the server continues without a panel. This replaces the current behaviour,
   where the same configuration silently exposes an open admin interface.
4. When the panel is bound to localhost with no password, it starts as it does
   today. Local-only access stays convenient.

Basic authentication over plain HTTP encodes the password rather than encrypting
it. The egg description must state that the panel belongs behind BadgerPanel's
proxy or an SSH tunnel, and must not imply the password alone makes it safe.

### Tests

The repository has no test project. Add `FusionDedicated.Tests` (xUnit) as part
of this sub-project, covering:

- a request with no `Authorization` header is rejected
- a request with a wrong password is rejected
- a request with correct credentials is accepted
- a malformed `Authorization` header is rejected rather than throwing
- the comparison helper returns correct results for equal, differing and
  different-length inputs. Its timing behaviour is not asserted, because timing
  tests are flaky. Correctness is tested, and the fixed-time property is a
  review requirement on the implementation.
- the bind refusal triggers for a non-localhost host with an empty password
- the bind proceeds for localhost with an empty password

---

## B. Container-aware resource reporting

### Problem

`ResourceMonitor.ReadHost()` reads `/proc/loadavg` and `/proc/meminfo`. Inside a
container those describe the whole node, so the panel's memory graph would report
the node's total rather than the server's limit. `Environment.ProcessorCount`
reports the node's cores for the same reason.

### Design

Read the container's own limits first, and fall back cleanly:

1. **cgroup v2** — `/sys/fs/cgroup/memory.max`, `/sys/fs/cgroup/memory.current`,
   `/sys/fs/cgroup/cpu.max`
2. **cgroup v1** — `/sys/fs/cgroup/memory/memory.limit_in_bytes`,
   `memory.usage_in_bytes`, `cpu.cfs_quota_us` with `cpu.cfs_period_us`
3. **`/proc`** — unchanged, for bare metal

A cgroup memory limit reading `max`, or a value at or above the host total, means
no limit is set and the `/proc` figures are correct. CPU count derives from the
cgroup quota divided by its period, rounded up, falling back to
`Environment.ProcessorCount`.

`HostStats` gains a field recording which source was used, so the panel can label
the figures as the container's or the host's rather than leaving it ambiguous.

The existing catch-all stays. Resource reporting must never be able to stop the
server.

### Tests

Parsing is pure and testable against fixture strings:

- cgroup v2 `memory.max` holding a byte count, and holding `max`
- cgroup v2 `cpu.max` holding `"200000 100000"`, and holding `"max 100000"`
- cgroup v1 quota and period pairs, including the `-1` unlimited sentinel
- fallback selection when no cgroup files are present

---

## E. Release workflow

### Problem

Without a published binary the install script needs the .NET SDK, which makes
installs slow and forces the SDK into the runtime image.

### Design

A GitHub Actions workflow on the fork, triggered by a tag matching `v*`, running
a `linux-x64` framework-dependent publish.

It packages the output as `fusion-dedicated-linux-x64.tar.gz` and attaches it to
the release, alongside a `SHA256SUMS` file the install script verifies.

`libsteam_api.so` is not bundled. The install script fetches it separately, as
described below.

---

## D. Runtime image, entrypoint and egg

### Image

`BadgerPanelYolks/games/fusion/`, holding a `Dockerfile` and its own
`entrypoint.sh`. Every yolk under `games/` carries its own entrypoint rather than
sharing one, so this follows suit. Conventions from the surrounding yolks:
`container` user at `/home/container`, `tini` as `ENTRYPOINT`, `STOPSIGNAL
SIGINT`, and `COPY --chown=container:container ./entrypoint.sh /entrypoint.sh`
with `CMD ["/entrypoint.sh"]`.

Base: `debian:bookworm-slim`. Contents:

- .NET 9 runtime, not the SDK
- Xvfb
- the Steam client bootstrap
- `jq` for the config merge, `curl`, `tini`

No game files are ever downloaded. The image is therefore far smaller than the
Proton-based egg, which installs BONELAB itself.

### Install script

Runs in `ghcr.io/parkervcp/installers:debian` and writes to `/mnt/server`:

1. Download the release tarball from the fork, verify it against `SHA256SUMS`,
   unpack it.
2. Download `Steamworks.NET-Standalone_2024.8.0.zip` from the Steamworks.NET
   GitHub release and extract `OSX-Linux-x64/libsteam_api.so` beside the binary.
   This version matches the `PackageReference` in `FusionDedicated.csproj`, and
   the download is public with no partner login.
3. Write `server.json` from `server.example.json` if it does not already exist,
   leaving an existing file untouched.

### Entrypoint

Order matters, because `SteamAPI.Init()` fails unless a signed-in Steam client is
already running:

1. Start Xvfb on `:99` and export `DISPLAY`.
2. Start the Steam client logged in from `STEAM_USER` and `STEAM_PASS` with the
   browser disabled, then wait for the process to appear, with a timeout that
   fails loudly.
3. Merge environment variables into `server.json` with `jq`, per the split below.
4. Replace the shell with the server process so it receives signals directly.

Steam's networking library can assert and take the process down. Pterodactyl
restarts the container, which reruns this sequence, so no in-container supervisor
is needed. The upstream systemd units have no equivalent here and are not used.

### Config split

Environment variables overwrite only their own keys. Every other key in
`server.json` survives the merge untouched.

Environment-owned: `ServerName`, `Description`, `MaxPlayers`, `Privacy`,
`VersionMajor`, `VersionMinor`, `LevelBarcode`, `LevelTitle`, `MaxEntities`,
`AntiSpamEnabled`, `SpawnBurstLimit`, `MaxEntitiesPerPlayer`, `DashboardPort`,
`DashboardHost`, `DashboardUser`, `DashboardPassword`, `RconPort`,
`RconPassword`, `LogDirectory`.

Volume-owned: `Bans`, `ModCatalog`, `Levels`, `ServerCode`.

`ranks.json` is a separate file and is volume-owned, except that
`OWNER_STEAMIDS` and `OPERATOR_STEAMIDS` are merged into it at boot without
removing entries added by console, RCON or a hand edit.

A value edited in the Fusion web panel that belongs to the environment half
applies immediately and reverts on the next restart. This is a consequence of the
chosen split and must be stated in the egg description.

### Egg

`egg-bonelab-fusion-headless.json`, beside the existing Proton egg.

- Startup done regex: `Lobby published`
- Stop: `^C`, matching the `CancelKeyPress` handler in `Program.Main`
- Primary allocation: the dashboard port. **Steam Datagram Relay needs no inbound
  ports for gameplay**, which is unusual for a game egg and belongs in the
  description. The only listening ports are the panel and, if enabled, RCON.
- Secondary allocation: RCON, required only when `RCON_PASSWORD` is set
- Disk minimum: roughly 3 GB, for Steam's runtime rather than for game files
- Variables: the environment-owned keys above, plus `STEAM_USER`, `STEAM_PASS`,
  `RCON_PASSWORD`, `OWNER_STEAMIDS` and `OPERATOR_STEAMIDS`

`STEAM_PASS`, `DashboardPassword` and `RCON_PASSWORD` are marked not viewable in
the panel.

---

## C. Client-created entity tracking

### Problem

`HandleMessage` dispatches nine tags and blind-relays everything else. Entities a
client creates without a spawn request, such as picking up a scene prop or using
the constrainer, are relayed but never registered. They do not appear in the
panel, do not count toward limits, and "Clear every entity" cannot remove them.

### Design

`TrackEntityPose` already parses tag 17 and extracts an entity ID. A pose update
carrying an ID at or above `EntityRegistry.FirstEntityId` that is absent from the
registry is a client-created entity announcing itself.

The method currently ignores the sender and must take the `ConnectedPlayer` so
discovered entities get an owner. Tags 9 and 10 (grab and release) and 15 and 16
(ownership) carry entity references and can feed the same path.

`TrackedEntity` gains `Discovered`. A discovered entity has no barcode, because
pose updates do not carry one, and displays with a distinct label rather than a
fabricated name.

### The risk

A scene prop that a player picks up receives a network ID exactly like a spawned
prop does. The two are indistinguishable at this layer. Despawning a scene prop
could desynchronise every connected client.

The rollout is therefore staged, and the stages ship separately:

**Stage 1 — discover and display.** Discovered entities are registered, shown in
the panel and counted separately. They are excluded from automatic culling and
from "Clear every entity". No existing behaviour changes, so this is safe to ship
before any live test.

**Stage 2 — opt-in removal.** A configuration flag, off by default, includes
discovered entities in "Clear every entity". Never in automatic culling. This
stage is validated against a live client before it is considered done.

If stage 2 desynchronises clients, stage 1 still delivers visibility and the
remainder is documented as a limit. That is a better outcome than the current
state, where these entities are invisible.

### Tests

`EntityRegistry` is pure logic:

- a pose for an unknown ID at or above 256 registers a discovered entity
- a pose for an unknown ID below 256 is ignored, because that range is player rigs
- a pose for a known ID updates position and does not mark it discovered
- discovered entities are excluded from `CullStale` and `CullOrphans`
- `ClearAllEntities` excludes discovered entities when the flag is off, and
  includes them when it is on
- ownership transfer on departure treats discovered entities the same as tracked ones

---

## F. Command surface: console, RCON and a ranks file

### Problem

Fusion's moderation vocabulary is fixed by its wire format:

```
Unknown = 0, Kick = 1, Ban = 2, TeleportToThem = 3, TeleportToMe = 4
```

There is no promote or demote command, so a stock client never sends one and the
in-game menu has no button for it. The outbound half already works, because the
server publishes each player's rank through the `PermissionLevel` metadata key
and clients read it. Only an inbound channel is missing.

The web panel has `/api/permission`, which requires leaving the game and reaching
a separate web interface.

### Why an out-of-band channel is required, not merely convenient

`HandlePermissionCommand` denies any action where
`target.Permission >= sender.Permission`. An Owner therefore cannot act on
another Owner, so no in-game path can ever create a second Owner. A channel that
carries no rank is the only way to grant that first or second Owner, which is
what the console and RCON provide. Commands arriving on either transport are
always permitted, because whoever holds the console or the RCON password already
controls the process.

### Design

One parser, two transports.

**`CommandProcessor`** owns parsing and dispatch. It takes a command line, returns
a text result, and calls the same methods the web panel already calls. It knows
nothing about where the line came from, which is what keeps the two transports
from duplicating logic.

**Transport 1, stdin.** A reader task parses one line per command. Pterodactyl
pipes its console straight to the process, so BadgerPanel gets a command surface
with no extra port and no auth.

**Transport 2, RCON.** A TCP listener speaking the Source RCON protocol: packet
framing, `SERVERDATA_AUTH`, `SERVERDATA_EXECCOMMAND`. This makes the server work
with `rcon-cli`, BadgerPanel's RCON features, and Discord bots. It stays disabled
unless `RconPassword` is set, and a failed authentication closes the connection
as the protocol requires. Because it is network-exposed, it gets the same care as
the dashboard password: fixed-time comparison, and a refusal to listen with an
empty password.

Commands operate on a SteamID64 or on a connected player's name, resolving names
case-insensitively and refusing an ambiguous match rather than guessing. A
SteamID that belongs to nobody currently connected is still valid for rank and
ban commands, which apply at that player's next join.

### The ranks file

The rank roster moves out of `server.json` into its own `ranks.json`, so it can
be edited over SFTP without touching configuration, and so a malformed edit
cannot take the whole config with it.

```json
{
  "76561198000000000": { "rank": "OWNER",    "name": "spudgun" },
  "76561198000000001": { "rank": "OPERATOR", "name": "someone else" }
}
```

The `name` field is for the operator's reference and is never trusted for
identity. The file is watched and reloaded when it changes, so a hand edit
applies without a restart. A parse failure leaves the previous roster in place
and logs the error, because dropping every rank because of a stray comma would be
worse than ignoring the edit.

`OWNER_STEAMIDS` and `OPERATOR_STEAMIDS` egg variables are merged into this file
at boot as comma-separated lists. Anyone listed there holds that rank from their
first join, with no console command and no web panel. Migration from an existing
`server.json` happens once, on first start after upgrading.

Because ranks apply at join ([FusionServer.cs:482]) and `SetPermission`
broadcasts the change to connected clients immediately, a promotion takes effect
mid-session. The promoted player sees their rank change and their menu update
without reconnecting.

Initial command set:

| Command | Effect |
|---|---|
| `promote <who> <guest\|default\|operator\|owner>` | Sets rank, persisted against the SteamID |
| `kick <who> [reason]` | Disconnects a connected player |
| `ban <who> [duration] [reason]` | Bans, permanently or for a duration |
| `unban <steamid>` | Lifts a ban |
| `mute` / `unmute <who>` | Voice mute, see section G |
| `players` | Lists connected players with rank and entity count |
| `purge <who>` | Removes that player's entities |
| `level <barcode> [title]` | Switches map |
| `say <message>` | Deferred until a text channel is confirmed to exist |
| `help` | Lists commands |

Unknown input is answered with a short error rather than being ignored, because a
silent console is indistinguishable from a hung one.

`say` is listed but not implemented in this phase. Inspecting `LabFusion.dll`
directly found no text-chat message type, and `NetworkNotifications` turned out to
be local-only popups rather than a server-to-client text channel. It stays out
until such a channel is confirmed to exist.

### Tests

Parsing is pure and gets thorough coverage:

- each command parses with and without its optional arguments
- an unknown command produces an error rather than throwing
- name resolution is case-insensitive, and an ambiguous name is refused
- an unparsable rank name is refused rather than defaulting to a rank
- a malformed duration is refused rather than becoming a permanent ban
- a rank command naming a SteamID for nobody connected is accepted and reported
  as applying at next join

The ranks file:

- a valid file loads every entry
- a malformed file leaves the previous roster in place and logs
- an edit on disk is picked up without a restart
- `OWNER_STEAMIDS` merges without dropping existing entries
- migration from `server.json` runs once and is not repeated

RCON, tested against the protocol rather than against a client library:

- a correct password authenticates, a wrong one closes the connection
- a command before authentication is refused
- request IDs are echoed as the protocol requires
- a body longer than one packet is handled
- the listener refuses to start with an empty password

---

## G. Moderation and anti-grief

### Why the existing mod cannot be loaded

`Z:\Dev\BoneLabAntiNuke` is a MelonLoader mod. It declares
`[MelonGame("Stress Level Zero", "BONELAB")]` and
`[MelonProcessAttribute("BONELAB_Steam_Windows64.exe")]`, and works by Harmony-
patching `LabFusion.Network.*Message.OnHandleMessage` inside the running IL2CPP
game process. Its dependencies are `MelonLoader`, `Il2Cpp*`, `UnityEngine.*`,
`LabFusion` and `BoneLib`, none of which exist outside BONELAB and none of which
can be supplied to a .NET 9 console application. A `Mods/` directory loading that
assembly would fail at resolution on the first load.

The mod exists because a client-hosted Fusion lobby has no server-side authority,
so protection has to be patched into whichever player is hosting. This relay is
that authority. Every message the mod patches has a seam in `HandleMessage`,
which currently dispatches nine tags and blind-relays the rest.

Porting rather than loading also produces a better result. Rules enforced here
apply to **unmodded clients**, and a griefer cannot bypass them by declining to
install anything.

### G1. Barcode blocklist

`BarcodeMatcher.AlwaysBlocked` is a plain `HashSet<string>` with no game
dependencies, so it ports across unchanged. It becomes a built-in blocklist
checked before the user-editable `BlacklistedBarcodes`, preserving the layering
the mod already uses: the built-in list cannot be whitelisted away through
configuration.

`HandleSpawnRequest` already consults `Config.BlacklistedBarcodes`, so this seeds
and hardens an existing check rather than adding a new one. A denied spawn is
logged with the barcode and the player.

### G2. Player controls

- **Voice mute.** Tag 67 carries voice. A muted player's voice packets are
  dropped at the relay instead of being broadcast, so muting works against a
  stock client with nothing installed. Mute state is per-session unless the
  player is also given a persistent flag.
- **Temporary bans.** `Bans` entries gain an optional expiry. An expired ban is
  ignored at join and swept from the list. Existing entries with no expiry stay
  permanent, so the change is backward compatible.
- **Whitelist mode.** An optional mode where only listed SteamIDs may join. Off
  by default. When on, an unlisted player is refused at the join handshake with a
  clear reason rather than being silently dropped.

### G3. Message-type gates

The eight message types the mod patches are relayed blind today. Each needs a
parser written against the wire format and a rule applied before relaying:

`PlayerRepTeleport`, `PlayerRepDamage`, `PlayerRepAvatar`, `LevelLoad`,
`SlowMoButton`, `ConstraintCreate`, `RPCMethod`, `InventorySlotInsert`.

This is the largest and least certain part of the work. Wire formats for these
tags are not yet implemented in `Protocol/`, so each has to be derived and then
confirmed against live traffic. The watcher logic in the mod
(`ExplosiveProbe`, `GodmodeWatcher`, `MovementWatcher`) depends on
`Il2CppSLZ.Marrow.Warehouse` and `UnityEngine` types, so it is rewritten against
wire data rather than copied.

Each gate ships independently and defaults to relaying unchanged. A gate that
cannot be validated against a live client stays off and is documented, rather
than being enabled on the assumption it works.

### G4. Audit log

Every moderation action records who acted, on whom, what, when, why, and through
which channel of console, panel or in-game. Written to its own file separate from
the server log so it survives log rotation and is readable on its own, and
surfaced as a panel tab.

### Tests

- a built-in blocked barcode is denied even when present in `BlacklistedBarcodes`
- a client-supplied `PermissionLevel` in a metadata request is ignored, and the
  rank held by the server is what gets broadcast
- a muted player's voice packets are dropped and their other packets still relay
- an expired ban does not prevent a join, and an unexpired one does
- a ban with no expiry stays permanent
- whitelist mode refuses an unlisted SteamID and admits a listed one
- each message gate defaults to relaying unchanged when disabled
- every moderation path writes exactly one audit entry, with the right channel

---

## Sequencing

Four phases. Each ends somewhere worth stopping.

**Phase 1 — foundations.** No game and no container needed, all unit-testable.

1. **A** — test project and dashboard authentication
2. **B** — container-aware resources
3. **G1** — barcode blocklist port
4. **F1** — `CommandProcessor`, the stdin transport, and `ranks.json`

**Phase 2 — ship it.** Produces something running on BadgerPanel, which makes
everything after it testable in the real environment.

5. **E** — release workflow
6. **F2** — the RCON transport, which needs the egg's second port allocation
7. **D** — image, entrypoint, egg, then a real deployment

**Phase 3 — player controls.**

8. **G2** — mute, temporary bans, whitelist
9. **G4** — audit log

**Phase 4 — live protocol work.** Both need a real client connected and both
expect iteration.

10. **C** — entity tracking, stage 1 then stage 2
11. **G3** — message-type gates, one at a time

Phase 2 lands deliberately early. Deploying before the protocol work means the
uncertain parts are validated where they will actually run.

## Risks

| Risk | Handling |
|---|---|
| Scene props are indistinguishable from spawned props | Staged rollout; stage 1 changes no behaviour |
| Unattended Steam login fails in a container | Fails loudly with a timeout; falls back to an x11vnc login flow if it proves unreliable |
| Fusion updates break version compatibility | `VersionMajor` and `VersionMinor` are environment variables, so operators can track a new Fusion build without a new image |
| Steam's runtime consumes the disk quota | Egg documents a 3 GB minimum |
| Upstream is two commits old with no external validation | Work stays on a branch against a tracked `upstream` remote |
| Wire formats for the eight gated message types are unknown | Each is derived and validated separately. A gate that cannot be validated stays off and is documented. |
| Console commands need stdin to survive the entrypoint | The entrypoint replaces the shell with the server process rather than backgrounding it, so stdin is inherited. Covered by a deployment check in phase 2. |
| Anti-grief rules now exist in two codebases | The relay copy is authoritative for servers running it. The mod stays useful for client-hosted lobbies. Barcode lists should be kept in step deliberately, not assumed to match. |
| Fusion has no known text-chat tag | Confirmed by inspecting `LabFusion.dll`: no text-chat message type exists and `NetworkNotifications` is local-only. `say` is specified but not implemented. |
| RCON is a network-exposed auth surface | Disabled unless a password is set, refuses to listen with an empty one, fixed-time comparison, and its own allocation so it can be firewalled separately from the panel. |
| No in-game promote button is possible on stock clients | Confirmed from `LabFusion.dll`: `PermissionCommandType` is only KICK, BAN and the two teleports. Ranks apply at join and `SetPermission` broadcasts live, so console and RCON promotion takes effect mid-session without a reconnect. |
| A client could request its own rank via player metadata | `PlayerMetadataRequestMessage` exists and permission is stored as metadata, which is the permission-spoof attack. The server must never honour a client-supplied `PermissionLevel`. Covered by a test in G. |
