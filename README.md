# Fusion Dedicated

A headless dedicated server for [BONELAB Fusion](https://github.com/Lakatrazz/BONELAB-Fusion).

No game, no VR headset, no host player. It runs on any spare Linux machine, appears
in the in-game server browser like a normal lobby, and keeps running when everyone
leaves. Unmodified Fusion clients connect to it without knowing the difference.

Includes a web control panel for moderation, map switching, live metrics and spam
protection.

---

## Why this is possible

Fusion has no master server. Lobbies *are* Steam lobbies, and the in-game browser is
a Steam lobby query filtered on a handful of metadata keys. Nothing has to bless a
server for it to be listed — it creates a Steam lobby, writes the same keys, and
clients find it.

The harder question is physics, and the answer is that there is none to do. In Fusion
**only an entity's owner simulates it**; the host relays everyone else's state
untouched. A server that never takes ownership of anything therefore never simulates
anything. It only has to:

- accept connections and run the join handshake
- allocate the small IDs players are addressed by
- remember what exists, so late joiners can be caught up
- forward packets to the right people

Which is why it holds eight players and a thousand props at roughly 2% of one CPU
core and 150 MB of RAM.

The wire format in `FusionDedicated/Protocol/` was derived by reading Fusion's source
and confirmed against captured traffic from a real client.

---

## Requirements

| | |
|---|---|
| OS | Any Linux with systemd (x64) — desktop or headless |
| Runtime | .NET 9 |
| Packages | Xvfb, Steam |
| Steam account | any account owning **SteamVR (app 250820)** — it is free |
| Steamworks SDK | `libsteam_api.so`, supplied once |

`install.sh` detects your distribution and offers to install what is missing. If you
prefer to do it yourself:

| Distro | Command |
|---|---|
| Arch / Manjaro | `sudo pacman -S --needed dotnet-sdk xorg-server-xvfb steam` |
| Debian / Ubuntu | `sudo apt install dotnet-sdk-9.0 xvfb steam-installer` |
| Fedora / RHEL | `sudo dnf install dotnet-sdk-9.0 xorg-x11-server-Xvfb steam` |
| openSUSE | `sudo zypper install dotnet-sdk-9.0 xorg-x11-server-extra steam` |

If .NET 9 is not packaged for your distribution, the official installer works
anywhere: <https://dot.net/v1/dotnet-install.sh>

**Xvfb is needed even on a desktop.** The Steam client refuses to run without a
display, and the server runs it on a virtual one so it never depends on you being
logged in to a graphical session.

**Use a separate Steam account.** The server signs in and stays signed in, and Steam
allows only one active session per account — running this on your everyday account
will sign you out of your own games.

**Why SteamVR's app id?** Fusion initialises Steamworks under app 250820 rather than
BONELAB's id, so its lobby metadata and relay traffic live there. This server does the
same, which is exactly what lets the two interoperate. Nothing is spoofed.

---

## Install

```bash
git clone https://github.com/AndreikaKopeika/fusion-dedicated.git
cd fusion-dedicated
./install.sh
```

The script verifies prerequisites, builds, and writes three systemd **user** units —
no root required. It is safe to re-run; an existing `server.json` is left untouched.

### Supplying libsteam_api.so

Valve's redistributable is not bundled here. Download the
[Steamworks SDK](https://partner.steamgames.com/downloads/list) (a free Steam account
is enough) and either:

```bash
STEAMWORKS_SDK=/path/to/steamworks_sdk ./install.sh
```

or copy `redistributable_bin/linux64/libsteam_api.so` into the install directory by
hand.

### Signing in to Steam

Steam Guard needs a human once. The installer writes a helper that handles both
cases:

```bash
~/fusiondedicated/steam-login.sh
```

**On a desktop**, it opens Steam on your normal session — sign in and close it.

**On a headless machine** there is no screen to sign in at, so it starts the virtual
display and exposes it over VNC on loopback. Tunnel in from your own machine:

```bash
ssh -L 5900:localhost:5900 user@your-server
```

then point any VNC viewer at `localhost:5900`, sign in, and press Ctrl+C on the
server. `x11vnc` is required for this path only.

Credentials are cached afterwards and the services look after themselves.

### Running

```bash
systemctl --user enable --now fusion-xvfb fusion-steam fusion-server
journalctl --user -u fusion-server -f
```

To survive reboots and logouts:

```bash
sudo loginctl enable-linger $USER
```

The three units are deliberately separate: Steam is supervised by a wrapper that
blocks while it lives, because Steam's launcher forks and returns — a naive unit
restarts it forever. The relay is tied to Steam with `PartOf`, so a Steam crash
rebuilds both.

---

## The control panel

Listens on `localhost:8778` by default. Reach it through an SSH tunnel:

```bash
ssh -L 8778:localhost:8778 user@your-server
```

then open `http://localhost:8778`.

| Tab | |
|---|---|
| Overview | players, entities, traffic, live log |
| Players | rank, kick, ban, purge a player's props |
| Map | 25 vanilla levels, plus modded maps by barcode |
| Analytics / Resources | CPU, memory, players, bandwidth — 10 minutes to a month |
| Ranks / Bans | persistent, keyed by SteamID |
| Settings | gameplay rules, permission gates, spam protection, restart |

### ⚠️ There is no authentication

Anyone who can reach port 8778 can kick, ban, restart the server, wipe the world and
change the map. No login, no token.

Keep `DashboardHost` set to `localhost` and tunnel in. Setting it to `"+"` publishes
an unauthenticated admin interface on every interface — acceptable on a network you
control, never on a machine with a public IP.

---

## Permissions

Mirrors Fusion's own model: `Guest (-1) · Default (0) · Operator (1) · Owner (2)`.

Ranks are stored against SteamIDs and applied at join. Each action has a minimum rank:

| Action | Default requirement |
|---|---|
| Dev tools, constrainer, custom avatars | Default |
| Kicking, banning, teleportation | Operator |

Clients hide buttons they believe you may not press, but **the server re-checks every
moderation command** before acting, and refuses when the target ranks at or above the
person asking.

---

## Spam protection

A spawn flood costs the server almost nothing — it simulates nothing — but every
*client* must instantiate each prop. Enough at once and an entire lobby drops while
the server idles at 2% CPU. The limits are therefore sized for what clients survive,
not what the server survives.

Defaults: 25 spawns per 5 seconds, 300 entities per player, 3 strikes. Early strikes
only delete the offending props; a kick follows repeated attempts. Props inherited
from players who left do not count against whoever inherited them.

Across one night of testing (140 joins, peaks of 8–12 players) the guard removed
3,254 props and kicked 4 people.

## Keeping the world clean

Props are not kept forever, and this matters if you plan to build something.

When a player leaves, their props are handed to whoever is still connected so they
keep being simulated instead of freezing mid-air. The catch is that they stop being
ownerless, so ordinary orphan cleanup never sees them again and the world only grows.
Left unchecked it reaches the entity cap, and from then on **every spawn is silently
refused** — the player pulls the trigger and nothing happens.

So inherited props that have not moved for `InheritedTimeoutSeconds` (15 minutes by
default) are removed. Anything a player is actively using keeps sending position
updates and survives; only genuinely abandoned props age out. If the world is at
capacity anyway, the oldest abandoned props are evicted to make room rather than
refusing the spawn, and a player's own work is never taken to free space for someone
else.

Raise `InheritedTimeoutSeconds` if your server is for building rather than sandbox
chaos, but be aware what the ceiling costs: on a busy public server this went from
3,696 refused spawns over two days to zero.

---

## Configuration

`server.json` is created from `server.example.json` on first run, and rewritten
whenever a setting changes in the panel. It holds ban lists, the rank roster and the
learned mod catalogue — **all keyed by other people's SteamIDs**, which is why it is
gitignored.

| Key | Meaning |
|---|---|
| `VersionMajor` / `VersionMinor` | must match the Fusion build your players run |
| `Privacy` | 0 public, 1 private, 2 friends only, 3 locked |
| `LevelBarcode` / `LevelTitle` | the map clients are told to load |
| `LevelModId` | mod.io id of the current map; also supplies the server's picture in the browser |
| `DashboardHost` | `localhost` or `+` — see the warning above |
| `AntiSpamExemptLevel` | rank that bypasses the spawn guard (`Owner` by default) |
| `MaxEntities` | world-wide prop ceiling |
| `InheritedTimeoutSeconds` | how long an abandoned prop survives before cleanup |
| `LogDirectory` | append-only logs and `metrics.csv` for the graphs |

---

## What is tested, and what is not

An honest inventory, because a server that overstates this wastes an evening.

**Verified end to end:** join handshake, packet relaying, spawn and despawn,
ownership transfer, permissions and moderation, bans, map switching, the spawn guard,
settings persistence across restarts, and automatic recovery from both a killed
server process and a Steam client crash.

**Implemented but never confirmed working:** mod-info brokering. When a player lacks
a modded item, the server tries to forward the question to a connected player who has
it, then remembers the answer for future joiners. Across 140 joins it never once
found a holder, so treat it as untested rather than as a feature.

**Known limits:**

- Entities created directly by clients — picking up scene props, the constrainer —
  are relayed but not tracked, so "Clear every entity" cannot remove them.
- Entity IDs are a `ushort`. A busy server works through the range in roughly a week
  of continuous uptime and then reuses freed IDs. Culled entities are properly
  despawned on clients first, which is what makes reuse safe.
- Gamemodes are not implemented; the server presents itself as plain sandbox.
- Player IDs 0–255 are reserved by clients for player rigs, so props are allocated
  from 256 upward. Allocating below that corrupts player entities on every client.

---

## Platform

Any systemd Linux on x86-64, desktop or headless — the installer detects the
distribution and adapts. It has been run on Arch; other distributions use the same
mechanisms (systemd user units, Xvfb, the Steam client) and should work, but if
yours needs a tweak a PR to `install.sh` is welcome.

**Windows is not supported.** Nothing in the code is Linux-specific beyond reading
`/proc` for host statistics, and Steamworks.NET is cross-platform, but it has never
been run there and the installer is systemd-only — so no install path is claimed.

**Non-systemd Linux** works too, it just installs by hand: build with
`dotnet publish -c Release -r linux-x64 --self-contained false -o ~/fusiondedicated`,
drop `libsteam_api.so` beside it, start Xvfb and Steam yourself, then run
`LD_LIBRARY_PATH=. dotnet fusiondedicated.dll` with `DISPLAY` pointing at the virtual
display.

---

## Contributing

Issues and pull requests are welcome. Useful things to include in a bug report:

- the relevant section of `logs/server-YYYY-MM-DD.log`
- your Fusion version and the server's `VersionMajor`/`VersionMinor`
- whether players disconnected with `Closing Connection` (a normal exit) or
  `Timeout; remote problem` (a client that stopped responding) — the distinction
  matters a great deal when diagnosing

---

## License

[MIT](LICENSE).

Built against [BONELAB Fusion](https://github.com/Lakatrazz/BONELAB-Fusion) by
Lakatrazz and contributors (MIT), and uses
[Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) (MIT). This project is
independent and is not affiliated with or endorsed by them, or by Stress Level Zero.
All game content, and the protocol design itself, belong to their respective authors.
