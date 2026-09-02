# Fusion Dedicated

A headless dedicated server for [BONELAB Fusion](https://github.com/Lakatrazz/BONELAB-Fusion).

No game, no VR headset, no host player. It runs on any spare Linux machine, appears
in the in-game server browser like a normal lobby, and keeps running when everyone
leaves. Unmodified Fusion clients connect to it without knowing the difference.

Includes a web control panel for moderation, map switching, live metrics and spam
protection.

- **No port forwarding.** Traffic goes through Steam's relay network, so it works
  behind NAT with nothing opened on your router.
- **No mods for players.** They join with stock Fusion.
- **Runs on almost nothing.** Eight players and a thousand props sit at roughly 2% of
  one CPU core and ~150 MB of RAM.

---

## Contents

- [Why it needs Steam](#why-it-needs-steam)
- [Requirements](#requirements)
- [Setting up the Steam account](#setting-up-the-steam-account)
- [Install](#install)
- [First run](#first-run)
- [The control panel](#the-control-panel)
- [Permissions](#permissions)
- [Spam protection](#spam-protection)
- [Keeping the world clean](#keeping-the-world-clean)
- [Configuration](#configuration)
- [Troubleshooting](#troubleshooting)
- [What is tested](#what-is-tested-and-what-is-not)
- [Uninstall](#uninstall)

---

## Why it needs Steam

This is the part that surprises people, so it is worth explaining before you start.

**Fusion has no master server and no direct IP connections.** It borrows two things
from Steam instead:

1. **Lobbies.** A Fusion "server" is a Steam lobby carrying a handful of metadata
   keys, and the in-game browser is a Steam lobby query. To be listed at all, you
   must be able to create a Steam lobby, which means being a signed-in Steam client.
2. **Transport.** Players connect over Steam Datagram Relay, Valve's relay network.
   That is why no port forwarding is needed, and also why the server cannot simply
   open a socket and skip Steam.

So this server signs in to Steam and stays signed in. Nothing is spoofed and it is
not pretending to be a game, it uses the same mechanisms Fusion itself uses.

**Why SteamVR?** Fusion initialises Steamworks under **app ID 250820 (SteamVR)**
rather than BONELAB's own ID, so all its lobby metadata and relay traffic live under
that app. This server does the same, which is exactly what allows the two to find
each other. Steam only lets an app initialise if the signed-in account owns it -
hence the requirement below.

**You do not need BONELAB on the server account.** Only SteamVR, which is free.

---

## Requirements

| | |
|---|---|
| OS | Any Linux with systemd (x86-64), desktop or headless |
| Runtime | .NET 9 |
| Packages | Xvfb, Steam |
| Steam account | a **separate** account with **SteamVR** added, both free |
| Steamworks SDK | `libsteam_api.so`, supplied once |
| Network | nothing to open |

`install.sh` detects your distribution and offers to install what is missing. To do
it yourself:

| Distro | Command |
|---|---|
| Arch / Manjaro | `sudo pacman -S --needed dotnet-sdk xorg-server-xvfb steam` |
| Debian / Ubuntu | `sudo apt install dotnet-sdk-9.0 xvfb steam-installer` |
| Fedora / RHEL | `sudo dnf install dotnet-sdk-9.0 xorg-x11-server-Xvfb steam` |
| openSUSE | `sudo zypper install dotnet-sdk-9.0 xorg-x11-server-extra steam` |

If .NET 9 is not packaged for your distribution, the official installer works
anywhere: <https://dot.net/v1/dotnet-install.sh>

**Xvfb is required even on a desktop.** The Steam client refuses to start without a
display. The server runs it on a virtual one so it never depends on you being logged
in to a graphical session.

---

## Setting up the Steam account

Do this before installing, it is the step people miss.

### 1. Use a separate account

Steam allows **one active session per account**. If the server signs in as you, you
get signed out of your own games, and it keeps happening, because the server signs
back in automatically.

Create a second free account for the server. It costs nothing and needs no purchases.

### 2. Add SteamVR to that account

SteamVR is free. Signed in as the server account, open its store page and add it to
the library:

<https://store.steampowered.com/app/250820/SteamVR/>

You do not need a VR headset, and you do not need BONELAB on this account.

Without this the server starts and immediately fails with `SteamAPI.Init() returned
false`, because Steam will not let it initialise under an app the account does not
own.

### 3. Sign in once on the server machine

Steam Guard needs a human the first time. After installing (below), run:

```bash
~/fusiondedicated/steam-login.sh
```

On a desktop it opens Steam normally, sign in and close it. On a headless machine it
starts the virtual display and exposes it over VNC on loopback, printing the exact
SSH tunnel command to use. Credentials are cached afterwards and the services handle
themselves from then on.

---

## Install

```bash
git clone https://github.com/AndreikaKopeika/fusion-dedicated.git
cd fusion-dedicated
./install.sh
```

### What the installer does

1. **Detects your distribution** (Arch, Debian/Ubuntu, Fedora/RHEL, openSUSE) and
   checks for `dotnet` 9+, `Xvfb` and `steam`. For anything missing it shows the
   exact package command and asks first, it never installs behind your back, and
   declining simply prints the command for later.
2. **Builds** the server with `dotnet publish` into `~/fusiondedicated`
   (override with `FUSION_INSTALL_DIR`).
3. **Looks for `libsteam_api.so`** in the repo, `$STEAMWORKS_SDK` and
   `~/steamworks_sdk`. If it is missing it says where to get it rather than failing
   later for an unclear reason.
4. **Creates `server.json`** from `server.example.json`, leaving an existing one
   alone, re-running the installer is safe.
5. **Writes two helper scripts** into the install directory: `steam-login.sh` for the
   one-time sign-in, and `steam-supervisor.sh`, which keeps Steam under systemd's
   control (its launcher forks and exits, which would otherwise make systemd restart
   it in a loop).
6. **Writes three systemd user units**, `fusion-xvfb`, `fusion-steam`,
   `fusion-server`, no root required, then reloads the daemon.
7. **Checks lingering**, which is what lets user services start at boot without you
   logging in, and prints the one command needing `sudo` if it is off.

It does not start anything and does not touch your Steam account.

### Supplying libsteam_api.so

Valve's redistributable is not bundled here, because it is not ours to publish. Get
the [Steamworks SDK](https://partner.steamgames.com/downloads/list), a free Steam
account is enough, then either:

```bash
STEAMWORKS_SDK=/path/to/steamworks_sdk ./install.sh
```

or copy `redistributable_bin/linux64/libsteam_api.so` into `~/fusiondedicated/`.

---

## First run

After signing in to Steam:

```bash
systemctl --user enable --now fusion-xvfb fusion-steam fusion-server
```

Make it survive logout and reboots:

```bash
sudo loginctl enable-linger $USER
```

Watch it start:

```bash
journalctl --user -u fusion-server -f
```

A healthy start looks like this:

```
Steam: your-account-name (76561198...)
Waiting for the Steam relay network...
INFO  Relay socket listening as SteamID 76561198...
INFO  Lobby published: 109775242..., the server is visible in the browser
INFO  Control panel: http://<this-machine-ip>:8778/
```

`Lobby published` is the line that matters. Your server should now show up in
BONELAB's browser under the name from `server.json`.

**Set the version to match your players.** Clients refuse to join across a
major/minor mismatch, so `VersionMajor` and `VersionMinor` must match the Fusion
build people are running.

### Why three services

The split is deliberate:

- **`fusion-xvfb`** provides the virtual display Steam needs.
- **`fusion-steam`** runs Steam through a supervisor that blocks while it lives.
  Steam's launcher forks and returns immediately, so a naive unit would decide the
  service had finished and restart it every few seconds.
- **`fusion-server`** is the relay, tied to Steam with `PartOf`, if Steam goes, both
  are rebuilt. Steam's networking library does occasionally assert and take the
  process down with it; this is what recovers unattended, usually in under a minute.

---

## The control panel

Listens on `localhost:8778` by default. Reach it through an SSH tunnel:

```bash
ssh -L 8778:localhost:8778 user@your-server
```

then open <http://localhost:8778>.

| Tab | |
|---|---|
| Overview | players, entities, traffic, live log |
| Players | rank, kick, ban, purge a player's props |
| Map | 25 vanilla levels, plus modded maps by barcode |
| Analytics / Resources | CPU, memory, players, bandwidth, 10 minutes to a month |
| Ranks / Bans | persistent, keyed by SteamID |
| Settings | gameplay rules, permission gates, spam protection, restart |

Settings changes reach connected players immediately, no reconnect needed.

### ⚠️ There is no authentication

Anyone who can reach port 8778 can kick, ban, restart the server, wipe the world and
change the map. No login, no token.

Keep `DashboardHost` on `localhost` and tunnel in. Setting it to `"+"` publishes an
unauthenticated admin interface on every interface, acceptable on a network you
control, never on a machine with a public IP.

---

## Permissions

Mirrors Fusion's own model: `Guest (-1) · Default (0) · Operator (1) · Owner (2)`.

Ranks are stored against SteamIDs and applied at join, so they persist across
restarts. Each action has a minimum rank:

| Action | Default requirement |
|---|---|
| Dev tools, constrainer, custom avatars | Default |
| Kicking, banning, teleportation | Operator |

Clients hide buttons they believe you may not press, but **the server re-checks every
moderation command** before acting, and refuses when the target ranks at or above the
person asking.

---

## Spam protection

A spawn flood costs the server almost nothing, it simulates nothing, but every
*client* must instantiate each prop. Enough at once and an entire lobby drops while
the server idles at 2% CPU. The limits are therefore sized for what clients survive,
not what the server survives.

Defaults: 25 spawns per 5 seconds, 300 entities per player, 3 strikes. Early strikes
only delete the offending props; a kick follows repeated attempts. Props inherited
from players who left do not count against whoever inherited them.

Across one night of testing (140 joins, peaks of 8–12 players) the guard removed
3,254 props and kicked 4 people.

---

## Keeping the world clean

Props are not kept forever, and this matters if you plan to build something.

When a player leaves, their props are handed to whoever is still connected so they
keep being simulated instead of freezing mid-air. The catch is that they stop being
ownerless, so ordinary orphan cleanup never sees them again and the world only grows.
Left unchecked it reaches the entity cap, and from then on **every spawn is silently
refused**, the player pulls the trigger and nothing happens.

So inherited props that have not moved for `InheritedTimeoutSeconds` (15 minutes by
default) are removed. Anything a player is actively using keeps sending position
updates and survives; only genuinely abandoned props age out. If the world is at
capacity anyway, the oldest abandoned props are evicted to make room rather than
refusing the spawn, and a player's own work is never taken to free space for someone
else.

Raise `InheritedTimeoutSeconds` if your server is for building rather than sandbox
chaos, but know what the ceiling costs: on a busy public server this went from 3,696
refused spawns over two days to zero.

---

## Configuration

`server.json` is created from `server.example.json` on first run, and rewritten
whenever a setting changes in the panel. It holds ban lists, the rank roster and the
learned mod catalogue, **all keyed by other people's SteamIDs**, which is why it is
gitignored.

| Key | Meaning |
|---|---|
| `ServerName` / `Description` | shown in the browser; Unity rich text works (`<color=#4ae08c>`) |
| `VersionMajor` / `VersionMinor` | **must match** the Fusion build your players run |
| `Privacy` | 0 public, 1 private, 2 friends only, 3 locked |
| `MaxPlayers` | slots; Fusion addresses players with one byte, so 255 is the hard ceiling |
| `LevelBarcode` / `LevelTitle` | the map clients are told to load |
| `LevelModId` | mod.io ID of the current map; also supplies the server's picture in the browser |
| `MaxEntities` | world-wide prop ceiling |
| `InheritedTimeoutSeconds` | how long an abandoned prop survives before cleanup |
| `AntiSpamExemptLevel` | rank that bypasses the spawn guard (`Owner` by default) |
| `DashboardHost` | `localhost` or `+`, see the warning above |
| `LogDirectory` | append-only logs and `metrics.csv` for the graphs |

Most of these are editable in the panel; the file is the source of truth on restart.

---

## Troubleshooting

**`SteamAPI.Init() returned false`**
The account does not own SteamVR, or the Steam client is not running or not signed
in. Check `systemctl --user status fusion-steam`, and confirm app 250820 is in the
account's library.

**Server starts but never appears in the browser**
Look for `Lobby published` in the log. If it is missing, Steam is up but the lobby
was refused, usually a signed-out client. If it is present and players still cannot
see it, check `Privacy` in `server.json` and that `VersionMajor`/`VersionMinor` match
their Fusion build.

**Players connect, then immediately drop**
Almost always a version mismatch. The log records the rejection reason.

**`fusion-steam` restarts in a loop**
Steam failed to start under the virtual display. Check `/tmp/steam.log` and confirm
the one-time sign-in was completed on this machine.

**Everyone disconnects at once**
Check the log for `MASS DISCONNECT`. It lists each player's transport-level reason
and separates a clean exit (`Closing Connection`) from a fault
(`Timeout; remote problem`). Several timeouts together means clients are freezing,
which usually points at the number of props in the world rather than at the server.

**Nothing spawns any more**
The world hit `MaxEntities`. Abandoned props are evicted automatically; the panel's
World tab also has a manual "Clear every entity".

Logs live in `~/fusiondedicated/logs/server-YYYY-MM-DD.log`, kept separately from the
journal so they survive restarts.

---

## What is tested, and what is not

An honest inventory, because a server that overstates this wastes an evening.

**Verified end to end:** join handshake, packet relaying, spawn and despawn,
ownership transfer, permissions and moderation, bans, map switching, the spawn guard,
world cleanup, settings persistence across restarts, and automatic recovery from both
a killed server process and a Steam client crash.

**Implemented but never confirmed working:** mod-info brokering. When a player lacks
a modded item, the server tries to forward the question to a connected player who has
it, then remembers the answer for future joiners. Across 140 joins it never once
found a holder, so treat it as untested rather than as a feature.

**Known limits:**

- Entities created directly by clients, picking up scene props, the constrainer -
  are relayed but not tracked, so "Clear every entity" cannot remove them.
- Entity IDs are a `ushort`. A busy server works through the range in roughly a week
  of continuous uptime and then reuses freed IDs. Culled entities are properly
  despawned on clients first, which is what makes reuse safe.
- Gamemodes are not implemented; the server presents itself as plain sandbox.
- Player IDs 0–255 are reserved by clients for player rigs, so props are allocated
  from 256 upward. Allocating below that corrupts player entities on every client.
- The panel has no authentication.

---

## Platform

Any systemd Linux on x86-64, desktop or headless, the installer adapts to the
distribution. Developed and run on Arch; other distributions use the same mechanisms
and should work, but if yours needs a tweak a PR to `install.sh` is welcome.

**Windows is not supported.** Nothing in the code is Linux-specific beyond reading
`/proc` for host statistics, and Steamworks.NET is cross-platform, but it has never
been run there and the installer is systemd-only.

**Non-systemd Linux** works, it just installs by hand: build with
`dotnet publish -c Release -r linux-x64 --self-contained false -o ~/fusiondedicated`,
drop `libsteam_api.so` beside it, start Xvfb and Steam yourself, then run
`LD_LIBRARY_PATH=. dotnet fusiondedicated.dll` with `DISPLAY` pointing at the virtual
display.

---

## Uninstall

```bash
systemctl --user disable --now fusion-server fusion-steam fusion-xvfb
rm -f ~/.config/systemd/user/fusion-{server,steam,xvfb}.service
systemctl --user daemon-reload
rm -rf ~/fusiondedicated
```

The Steam account and its cached credentials are untouched; sign out through Steam
itself if you want those gone too.

---

## Contributing

Issues and pull requests are welcome. Useful things to include in a bug report:

- the relevant section of `logs/server-YYYY-MM-DD.log`
- your Fusion version and the server's `VersionMajor`/`VersionMinor`
- your distribution, if it is an install problem
- whether players disconnected with `Closing Connection` (a normal exit) or
  `Timeout; remote problem` (a client that stopped responding), the distinction
  matters a great deal when diagnosing

---

## License

[MIT](LICENSE). Attribution and third-party notices are in [NOTICE](NOTICE).

Built against [BONELAB Fusion](https://github.com/Lakatrazz/BONELAB-Fusion) by
Lakatrazz and contributors, and uses
[Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET). This project is
independent and is not affiliated with or endorsed by them, or by Stress Level Zero.
