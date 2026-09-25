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
- [Plugins](#plugins)
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
only delete the offending props; a kick follows repeated attempts. Only a player's own
spawns count against them. Props inherited from players who left, kept props, level
props, constraint ends and anything a plugin spawned never count and are never purged.
Creating a constraint is held to the same per-second spawn rate cap and spawn guard,
so constraint spam earns the same strikes, purge and kick.

`BlockHolsterDuplicates` (on) caps duplication rather than stopping it. A duplication
mod pulls an item out of a holster slot, asks for the same barcode again with source 0,
EntitySource None, and puts the copy back in the slot itself. The first such spawn is
allowed, because Fusion asks the same way for loot dropped by a destructible and for the
items a level puts into slots, and either can name something the player is already
carrying. Every later spawn of that barcode by that player is refused and strikes the
spawn guard, and each refusal keeps the barcode suspect for another
`HolsterDuplicateWindowSeconds` (10), so a spree costs the duper one copy and then stops
paying. The suspicion does not go back to the slots, because the mod returns the copy
without telling the server, so a refusal no longer depends on the server still having a
slot record. A player who stops for the length of the window starts clean again, which
means a patient duper who waits it out still gets one copy per window, six a minute at
the default and 360 an hour.

A strike from this rule purges nothing, since a dropped request left nothing behind, and
the kick comes at the usual `SpamStrikesBeforeKick`.

Two memories back it, each held per player and both dropped when the player leaves and
when the level changes. An item stays protected for `HolsterDrawSeconds` (3) after it
leaves a slot, because the copy request and the game's own message saying the slot is
empty are sent by two different mods on the same grab and arrive in either order. Set
either window to 0 to turn that half off: no draw is remembered at 0, and with the
duplicate window at 0 nothing is ever refused. Each player is tracked for at most 16
drawn items and 16 suspected barcodes, and past that it is the barcode nobody has
spawned for longest that is forgotten, so decoys cannot crowd out the one that matters.

The price of catching the copy is that a second genuine spawn of the same barcode inside
the window is refused and struck as well. Two loot drops of the same gun eight seconds
apart cost the player a strike, and only the first of the two has to find the gun in a
slot: once a barcode is suspect the slots are not asked about it again, so somebody who
has since thrown that gun away is refused all the same while the drops keep coming inside
the window. At most one strike is counted per `SpawnWindowSeconds`, and
`SpamStrikesBeforeKick` (3) of them kick. Raise `HolsterDuplicateWindowSeconds` to catch
more duping, or lower it to punish fewer honest players.

The rule has its own switch and does not read `AntiSpamEnabled`, so it still refuses and
strikes on a server with the rest of the spam guard turned off, and it applies at every
rank, `AntiSpamExemptLevel` and Owner included. It runs before the rank check and before
the blocklist, so listing `0` in `SpawningExemptSources` does not get a repeat past it,
and a blocklisted barcode that is holstered is logged here rather than as a blocklist
refusal. A holster record is kept only when a player reports it on their own body or on
a prop, never on somebody else's body, so nobody can plant one on another player and
have them struck over it. Only slots on a player's own body are searched, so an item
taken off a gun rack or a locker is covered for `HolsterDrawSeconds` and no longer than
that. A slot records the entity rather than the barcode, so a slot whose entity the
server cannot name is passed over.

Two limits are worth knowing before you rely on it. A modded client can erase its own
slot record with a message the server has no way to disprove, either an insert naming a
prop or a drop naming its own slot, and a later pull then records no draw either, so the
rule has nothing to arm on for that barcode until the item is holstered again through a
real hand drop. The mod this was written against does none of that, and a client can only
do it to itself, since a record is never written onto another player's body and taking
one off theirs only ever makes them harder to refuse. Refusals also pass through the
shared refusal limiter, so a client spamming them past `RefusalKickPerSecond` (50) is
removed for flooding first and the log names that reason instead of this one.

`MaxEntitiesPerPlayer` also caps the level props a player's own game reports by pose,
the props nobody spawned but that a client still tracks. Past the cap the server stops
tracking new ones for that player and logs it once per level.

Metadata changes, avatar swaps and RPC messages have allowances of their own, per
player per second: `MetadataPerSecond` (10), `AvatarSwapsPerSecond` (2) and
`RpcMessagesPerSecond` (250). A message over its allowance is dropped before anybody
else receives it, and the log totals each player's drops once a minute per player and
per kind of message. A dropped message is gone rather than delivered later, so if an
SDK map's levers, doors or other synced parts fall out of step, raise
`RpcMessagesPerSecond` or set it to 0. The message a player's game sends when it
finishes loading a level is never dropped and does not count against
`MetadataPerSecond`, so a busy player still receives the level's state. Nobody is
kicked for going over. Like the spawn guard, the allowances apply only while
`AntiSpamEnabled` is on and only to players below `AntiSpamExemptLevel`. Set one to 0
to lift that limit.

Ownership requests are held to `OwnershipRequestsPerSecond` (10) per player, which follows
`AntiSpamEnabled` and `AntiSpamExemptLevel` like the allowances above. An entity that has
just changed hands then keeps its new owner for `OwnershipHoldMilliseconds` (500) before it
may change again, at every rank and whether or not anti-spam is on. A client asks on every
physics contact, and one SWAT van that nine players all believed they owned took 2,384 owner
changes in a single minute, each of them snapping it to a different player's copy, which is
what a vehicle flinging about looks like. Somebody refused inside the window is still told
who owns it, so their view heals, and its owner, whoever sits in it and whoever holds it are
never held back. A rider whose game sends the vehicle's poses is held the same way against
another rider's, so two people in one van cannot trade it at the network tick. Both stay out
of the console: requests dropped over the allowance are totalled once a minute per player,
while a refusal by the hold writes one line per player every ten seconds and carries no
count, so it tells you a player was refused rather than how often. Set either to 0 to lift
that half.

A vehicle follows whoever sits in seat `DriverSeatIndex` (0), and only that seat's poses
take it. A passenger's game sends the vehicle's poses too, and while any seat could take it
a full SWAT transport traded the van on every network tick: each passenger's pose took it
off the driver, the driver's next pose took it back, and in between everybody else's poses
were thrown away as a non-owner's, which is the jitter riders saw and why a player stepping
out landed somewhere nobody else had them. A vehicle its owner left behind goes to its
driver first and only then to the other riders. A driver who stands up keeps the vehicle
while he is still simulating it, so it carries on moving rather than freezing, and a rider
who wants it is answered as soon as he is out of the seat. Walking far enough away makes his
game cull the vehicle and stop sending its poses, and that cull hands it to a rider on the
spot, so a van full of players does not hibernate where it stood. Set `DriverSeatIndex` to
the seat that drives a vehicle whose driver is not seat 0. It has to match the vehicle's real
driver seat: a client locks a vehicle to whoever sits in the seat the vehicle itself drives
from, so a wrong setting has the server handing ownership to a rider every client has locked
to somebody else.

A rider a plugin refuses a seat is then left alone for `SeatRefusalCooldownSeconds` (2)
before that seat is put to plugins again. Fusion registers the seat again while the rider
stands in its trigger, so one police SUV refused five players 342 times in four minutes, and
the egress answering each refusal put their rig into and out of the seat about three times a
second, which is what players mean by spassing out. The first attempt still stands them up
and tells everybody. An attempt inside the window is dropped where it arrives: no plugin is
asked, nothing is sent and nothing is relayed, so their own game may go on showing them
seated until they step out of the trigger. Nobody else sees them in the seat, the server
never records them in it and it never changes hands to them, so a driver a plugin refuses
still cannot drive. The attempt after the window is refused properly again. Refusals go to
the log file only, one line each, carrying a count of what was dropped since the last one.
Set it to 0 to refuse every attempt.

Hits from one player on another are held to `HitsPerSecond` (20), counted for each
pair of players, because a client can send a hit on every physics tick. A player's
hits on someone they are holding are always dropped, since those were knocking held
players out. The log totals each pair's dropped hits once a minute. These apply
while `ExtendedProtection` is on, to every rank.

An avatar swap carries the avatar's proportions and masses, and every other game gives
the model those numbers, so a modded client sending a huge mass flings whoever it
touches. A swap whose scale is over `MaxAvatarScale` (50), whose height is over 1.76
times that, whose arm, chest, head, leg or pelvis
weighs more than `MaxAvatarPartMass` (2500), whose whole avatar weighs under 1 or more
than `MaxAvatarMass` (5000), or whose length or radius is negative, is relayed with
those numbers pulled back inside the limits rather than dropped, so everybody sees the
avatar at the nearest size the server allows. The numbers come from what players here
actually wear: tiny avatars, a 40 times scale and a 1,649 kg avatar were all being
dropped at the first limits, so the limits moved rather than the players. A negative
scale axis mirrors the avatar rather than shrinking it, so only its size is held to the
limits and the sign is kept. Values no
real avatar has are dropped and are strikes: a number that is not a real number or is
too small to hold, a mass or a scale over 100000 either way, any other stat over a
billion either way, a negative mass, or a whole avatar with no mass.
`AvatarStrikesBeforeKick` (3) strikes inside `AvatarStrikeWindowSeconds` (60) kick the
player, so a big avatar that is only over the limits costs nothing. Joins are checked
the same way: one with a value no real avatar has is refused with "impossible avatar
stats", and one only over the limits is let in with its numbers pulled back inside
them. A limit set below its own minimum, such as a `MaxAvatarMass` under the 1 every
avatar weighs, is ignored rather than obeyed and named in the log at startup, since
obeying it would refuse every avatar on the server. There is no lower bound on scale:
an avatar ported from another game is built small on purpose to fix its units, and a
floor would raise only the copy other players are sent, so its wearer would look normal
to themselves and oversized to everybody else. This applies while
`ExtendedProtection` is on, to every rank. Whatever the settings, any message whose
prefix Fusion would read differently from the server is dropped and noted in the log
file only.

A moving prop's poses go to players further than `PoseThinDistance` (60 m) from it only
`FarPosesPerSecond` (5) times a second instead of about 20. Fusion only freezes a prop
after half a second without a pose, so far props keep moving, a little less smoothly.
Player movement and a prop's resting pose are always sent, and so is everything to a
player whose position is not known yet. Set either to 0 to send every pose to everyone.

A fly mod spawns its gun on the player's own machine through BoneLib, so the server never
sees the gun, only where their body goes. `FlightSpeed` (6 m/s) and `FlightWindowSeconds`
(2) set what counts as flying: holding that speed up or down for that long at an even pace.
Gravity is what tells the two apart. A jump, a fling off an explosion and a fall are all
pulled by it, so their speed changes by about 5 m/s inside each quarter of the window, while
a flight holds one speed throughout. So jumping, being flung tens of metres, falling off a
building and landing are all left alone, as are a ladder at about 1 m/s and a lift at 2 to 3.
A fall long enough to stop speeding up holds a speed above `FlightMaxFallSpeed` (25 m/s),
which is also left alone. Each flight is a warning naming the player, their speed and whether they hold a
Nimbus gun the server spawned. `FlightStrikesBeforeKick` (3) flights inside
`FlightStrikeWindowSeconds` (60) is a kick, and 0 strikes only logs them. `FlightExemptLevel`
(Operator) and above are never checked, and so is anybody sitting in a vehicle. Set
`FlightSpeed` to 0 to turn the whole check off.

Once a minute the log gets a line per player with what Steam measures of their
connection: ping, quality, send rate, bytes waiting and queue time. A ping over 250 ms,
quality under 90% or a queue over half a second makes it a warning on the console.

Steam refuses a send outright once a player's send buffer is full, and one evening of 19
players it refused 8,895 of them. A refused reliable message is now held rather than
thrown away, and goes out as soon as that player's buffer drains; anything reliable sent
to them in the meantime waits behind it, so ownership answers, spawns, despawns and
catch-up still arrive in order. `SendRetryQueue` (256) is how many messages one player may
have waiting, and the oldest are dropped when it is full, which the log file notes once.
Set it to 0 to drop a refused send where it stands. Lowering it while a queue is already
over the new number stops it growing rather than cutting it back, and the queue shrinks as
it drains. A player joining a backed-up server
keeps their catch-up in the catch-up queue instead, which has no cap, because nothing asks
for a catch-up message twice.

A player with `CongestedPendingBytes` (128 KB) or more still buffered on their connection
is sent no poses until that comes down, which leaves the line to the reliable traffic. The
measure is what Steam has taken and not yet put on the wire, which is what its 512 KB send
buffer counts, rather than everything in flight. Poses are a large share of the outbound
and the next one replaces the one that was skipped, so props and players catch themselves
up. Set it to 0 to send poses whatever is waiting. The log file names each player this
happens to, at most once a minute each, and sums up the retries, the drops and the held
back poses with what they weighed.

`CatchupMessagesPerSecond` (100) caps how many catch-up messages a player is sent each
second: the props, scene objects and constraints already in the world when they join,
the holsters and magazines after that, and the level's variables once they finish
loading. The first 100 go out at once and the rest follow at about 100 a second, so
900 level variables take about 8 seconds and a full world at the entity and variable
caps takes about 20. Raise it if joins to a quiet server feel slow, or set it to 0 to
send everything at once. It applies whether or not `AntiSpamEnabled` is on.

Across one night of testing (140 joins, peaks of 8–12 players) the guard removed
3,254 props and kicked 4 people.

---

## Keeping the world clean

Props are not kept forever, and this matters if you plan to build something.

When a player leaves, a vehicle goes to somebody sitting in it and a held prop to
somebody holding it. Everything else is left without an owner until somebody grabs or
bumps it, and is removed after `OrphanTimeoutSeconds` (2 minutes) if nobody does.
Handing a leaver's whole pile to one player froze that player's game: late in a busy
day that was about 590 props, and the heir timed out within 20 seconds.

Props a joiner adopts and props handed on stop being ownerless, so orphan cleanup never
sees them. Left unchecked the world reaches the entity cap, and from then on **every
spawn is silently refused**, the player pulls the trigger and nothing happens.

Magazines, and any crate whose name contains a word in `ShortLivedBarcodes` (`Mist` by
default, for spray paint mist), are removed `AmmoTimeoutSeconds` (1 minute) after they
last moved, even while their owner is still playing.

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

## Plugins

A plugin is a .NET assembly the server loads at startup. It can refuse a spawn or an
avatar, answer a client mod on its own message tag, and add its own tab to the
control panel. It runs inside the server process, so only install one you trust.

Plugins are off until you turn them on. Set **Plugins** to `true` in the server's
startup variables, or `PluginsEnabled` in `server.json` if you run outside a panel,
then restart.

Each plugin is a folder holding a `plugin.json` and the assembly it names. Put those
folders in `plugins/`, next to `fusiondedicated.dll`:

    plugins/
      police/
        plugin.json
        Police.dll
      avatars/
        plugin.json
        AvatarWhitelist.dll

`plugin.json` names the assembly to load:

    {
      "name": "police",
      "version": "1.0.0",
      "apiVersion": 1,
      "entry": "Police.dll",
      "description": "A roster who may use restricted avatars and equipment"
    }

Then either restart, or run `plugins reload` on the console or over RCON. Reloading
is a command rather than a watch on the folder, because a half-copied DLL would be
picked up and fail. `plugins` on its own lists what is loaded.

A plugin that throws is logged against its name and disabled after three faults,
so a bad plugin degrades the feature it owns instead of taking the server with it.
Anything it stores lives in `plugin-data/<name>.json`, beside `bans.json`, so
replacing a plugin folder does not take its saved data with it. A plugin that
still has a `data.json` inside its own folder has it copied across on the next
start, once, and the old file is left alone.

Ready-made plugins and the API to build your own are at
[fusion-server-mods](https://github.com/spudgun1001/fusion-server-mods).

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
| `Privacy` | 0 public, 1 private, 2 friends only, 3 locked; checked at join, see below |
| `MaxPlayers` | slots; Fusion addresses players with one byte, so 255 is the hard ceiling |
| `LevelBarcode` / `LevelTitle` | the map clients are told to load |
| `LevelModId` | mod.io ID of the current map; also supplies the server's picture in the browser |
| `MaxEntities` | world-wide prop ceiling |
| `InheritedTimeoutSeconds` | how long an abandoned prop survives before cleanup |
| `AmmoTimeoutSeconds` | how long a dropped magazine or short-lived effect survives after it last moved (60 by default, 0 leaves them to the other timeouts) |
| `ShortLivedBarcodes` | words in a crate name that put it on the ammo clock too (`["Mist"]` by default) |
| `AntiSpamExemptLevel` | rank that bypasses the spawn guard and the message allowances (`Owner` by default) |
| `BlockHolsterDuplicates` | refuses a repeat source None spawn of a barcode the player has holstered or just drew, at every rank (on by default) |
| `HolsterDrawSeconds` | how long an item a player has drawn still counts as holstered for that check (3 by default, 0 remembers no draws) |
| `HolsterDuplicateWindowSeconds` | how long a barcode stays suspect, so every spawn of it inside that time is refused (10 by default, 0 never refuses) |
| `MetadataPerSecond` | metadata changes each player may send per second (10 by default, 0 for no limit) |
| `AvatarSwapsPerSecond` | avatar swaps each player may send per second (2 by default, 0 for no limit) |
| `RpcMessagesPerSecond` | RPC variable and event messages each player may send per second (250 by default, 0 for no limit) |
| `LogRpc` | logs one line per RPC message received: sender, tag, length, first bytes, and whether it was dropped by budget, unreadable, or offered to plugins (off by default, capped at 30 lines a second) |
| `OwnershipRequestsPerSecond` | ownership requests each player may send per second (10 by default, 0 for no limit) |
| `OwnershipHoldMilliseconds` | how long an entity stays with its new owner before it may change hands again (500 by default, 0 lets every change through) |
| `SeatRefusalCooldownSeconds` | how long a rider a plugin refused a seat is left alone before that seat is put to plugins again (2 by default, 0 refuses every attempt) |
| `DriverSeatIndex` | the seat that drives a vehicle, whose poses take it and who inherits it (0 by default) |
| `HitsPerSecond` | hits one player may land on another per second (20 by default, 0 for no limit) |
| `MaxAvatarMass` / `MaxAvatarPartMass` | heaviest avatar a player may send, and heaviest body part (5000 and 2500 by default, 0 for no limit) |
| `MaxAvatarScale` | largest avatar scale a player may send, which also bounds height (50 by default, 0 for no limit); there is no smallest, since ported avatars are built small on purpose |
| `AvatarStrikesBeforeKick` | avatars with impossible stats inside `AvatarStrikeWindowSeconds` (60) that get a player kicked (3 by default, 0 never kicks) |
| `PoseThinDistance` | metres from a moving prop past which a player gets fewer of its poses (60 by default, 0 sends all) |
| `FarPosesPerSecond` | poses a second those far players get (5 by default, 0 sends all) |
| `FlightSpeed` | metres a second up or down that counts as flying (6 by default, 0 turns the check off) |
| `FlightWindowSeconds` | how long they must keep that speed to be called a flier (2 by default) |
| `FlightMaxFallSpeed` | a drop faster than this is a fall, not a flight (25 by default) |
| `FlightStrikesBeforeKick` | flights inside the window before a kick (3 by default, 0 only logs) |
| `FlightStrikeWindowSeconds` | how long a flight counts against a player (60 by default) |
| `FlightExemptLevel` | rank never checked for flying (`Operator` by default) |
| `CatchupMessagesPerSecond` | catch-up messages each joining player is sent per second (100 by default, 0 sends everything at once) |
| `SendRetryQueue` | reliable messages held for one player when Steam refuses a send (256 by default, 0 drops a refused send) |
| `CongestedPendingBytes` | bytes Steam still has unsent for a player, at or above which they are sent no poses (131072 by default, 0 sends poses whatever is waiting) |
| `DashboardHost` | `localhost` or `+`, see the warning above |
| `LogDirectory` | append-only logs and `metrics.csv` for the graphs |

Most of these are editable in the panel; the file is the source of truth on restart.

`Privacy` follows Fusion's own rules when a player joins: public and private let
anyone in, friends only admits Steam friends of the account the server runs as, and
locked admits nobody new. Owners and operators are refused like anybody else, with
Fusion's message "Server is private."

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
Almost always a version mismatch. The log records the rejection reason. A player told
"Server is private." was refused by `Privacy`. A connection that never asks to join
is closed after 15 seconds.

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
