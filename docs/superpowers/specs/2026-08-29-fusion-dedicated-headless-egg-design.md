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
- **C** — client-created entity tracking

Out of scope: gamemodes, the two non-defects above, any change to the Proton-based
egg that already exists in `BonelabFusionDedicated`.

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
`DashboardHost`, `DashboardUser`, `DashboardPassword`, `LogDirectory`.

Volume-owned: `Bans`, `Permissions`, `ModCatalog`, `Levels`, `ServerCode`.

A value edited in the Fusion web panel that belongs to the environment half
applies immediately and reverts on the next restart. This is a consequence of the
chosen split and must be stated in the egg description.

### Egg

`egg-bonelab-fusion-headless.json`, beside the existing Proton egg.

- Startup done regex: `Lobby published`
- Stop: `^C`, matching the `CancelKeyPress` handler in `Program.Main`
- Primary allocation: the dashboard port. **Steam Datagram Relay needs no inbound
  ports**, which is unusual for a game egg and belongs in the description.
- Disk minimum: roughly 3 GB, for Steam's runtime rather than for game files
- Variables: the environment-owned keys above, plus `STEAM_USER` and `STEAM_PASS`

`STEAM_PASS` and `DashboardPassword` are marked not viewable in the panel.

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

## Sequencing

1. **A** — test project and dashboard authentication
2. **B** — container-aware resources
3. **E** — release workflow
4. **D** — image, entrypoint, egg, then a real deployment on BadgerPanel
5. **C** — stage 1, then stage 2 with a live client

A and B need no game and no container. D produces something running on
BadgerPanel. C is last because it is the only part that cannot be finished
without a real client connected.

## Risks

| Risk | Handling |
|---|---|
| Scene props are indistinguishable from spawned props | Staged rollout; stage 1 changes no behaviour |
| Unattended Steam login fails in a container | Fails loudly with a timeout; falls back to an x11vnc login flow if it proves unreliable |
| Fusion updates break version compatibility | `VersionMajor` and `VersionMinor` are environment variables, so operators can track a new Fusion build without a new image |
| Steam's runtime consumes the disk quota | Egg documents a 3 GB minimum |
| Upstream is two commits old with no external validation | Work stays on a branch against a tracked `upstream` remote |
