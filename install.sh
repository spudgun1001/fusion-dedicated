#!/usr/bin/env bash
#
# Installs Fusion Dedicated as systemd user services.
#
# Works on any systemd distribution, headless or desktop. Everything runs as your
# own user — no root — except installing distribution packages, which the script
# offers to do and never does behind your back.
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$REPO_DIR/FusionDedicated"
INSTALL_DIR="${FUSION_INSTALL_DIR:-$HOME/fusiondedicated}"
UNIT_DIR="$HOME/.config/systemd/user"
DISPLAY_NUM="${FUSION_DISPLAY:-:99}"

say()  { printf '\033[36m==>\033[0m %s\n' "$1"; }
ok()   { printf '\033[32m  ✓\033[0m %s\n' "$1"; }
warn() { printf '\033[33m  !\033[0m %s\n' "$1"; }
die()  { printf '\033[31m  ✗\033[0m %s\n' "$1" >&2; exit 1; }

# ------------------------------------------------------------- distribution

PM=""; PM_INSTALL=""; PKG_XVFB=""; PKG_STEAM=""; PKG_VNC="x11vnc"; PKG_DOTNET=""

detect_distro() {
    local id="unknown"
    [ -r /etc/os-release ] && id="$(. /etc/os-release && echo "${ID_LIKE:-$ID}")"

    case "$id" in
        *arch*|*manjaro*)
            PM="pacman"; PM_INSTALL="sudo pacman -S --needed"
            PKG_XVFB="xorg-server-xvfb"; PKG_STEAM="steam"; PKG_DOTNET="dotnet-sdk"
            ;;
        *debian*|*ubuntu*)
            PM="apt"; PM_INSTALL="sudo apt install -y"
            PKG_XVFB="xvfb"; PKG_STEAM="steam-installer"; PKG_DOTNET="dotnet-sdk-9.0"
            ;;
        *fedora*|*rhel*)
            PM="dnf"; PM_INSTALL="sudo dnf install -y"
            PKG_XVFB="xorg-x11-server-Xvfb"; PKG_STEAM="steam"; PKG_DOTNET="dotnet-sdk-9.0"
            ;;
        *suse*)
            PM="zypper"; PM_INSTALL="sudo zypper install -y"
            PKG_XVFB="xorg-x11-server-extra"; PKG_STEAM="steam"; PKG_DOTNET="dotnet-sdk-9.0"
            ;;
        *)
            PM=""; PKG_XVFB="Xvfb"; PKG_STEAM="steam"; PKG_DOTNET=".NET 9 SDK"
            ;;
    esac
}

# Offers to install a missing package. Declining is fine — it just reports.
want() {
    local binary="$1" package="$2" why="$3"

    command -v "$binary" >/dev/null && { ok "$binary"; return 0; }

    warn "$binary is missing — $why"

    if [ -z "$PM" ]; then
        echo "      Install \"$package\" with your package manager, then re-run."
        return 1
    fi

    read -rp "      Install it now with: $PM_INSTALL $package ? [y/N] " reply

    if [[ "$reply" =~ ^[Yy]$ ]]; then
        $PM_INSTALL "$package" && { ok "$binary installed"; return 0; }
    fi

    echo "      Skipped. Run: $PM_INSTALL $package"
    return 1
}

# ------------------------------------------------------------- checks

detect_distro

say "Checking prerequisites${PM:+ (detected $PM)}"

command -v systemctl >/dev/null || die "systemd is required — this installer sets up user services."
[ -d /run/systemd/system ] || die "systemd is not running as init on this machine."

MISSING=0

if command -v dotnet >/dev/null; then
    DOTNET_MAJOR="$(dotnet --list-runtimes 2>/dev/null \
        | grep -oE 'Microsoft\.NETCore\.App ([0-9]+)' | grep -oE '[0-9]+$' | sort -rn | head -1 || echo 0)"

    if [ "${DOTNET_MAJOR:-0}" -ge 9 ]; then
        ok "dotnet (runtime $DOTNET_MAJOR)"
    else
        warn "dotnet is present but too old (found $DOTNET_MAJOR, need 9+)"
        echo "      ${PM:+$PM_INSTALL $PKG_DOTNET   or  }https://dot.net/v1/dotnet-install.sh"
        MISSING=1
    fi
else
    want dotnet "$PKG_DOTNET" "the server is a .NET 9 application" || MISSING=1
fi

want Xvfb "$PKG_XVFB" "Steam needs a display, even without a monitor" || MISSING=1
want steam "$PKG_STEAM" "the server signs in through the Steam client" || MISSING=1

[ "$MISSING" -eq 0 ] || die "Install the packages above and re-run this script."

# ------------------------------------------------------------- build

say "Building"
dotnet publish "$PROJECT_DIR" -c Release -r linux-x64 --self-contained false \
    -o "$INSTALL_DIR" --nologo -v quiet
mkdir -p "$INSTALL_DIR/logs"
ok "built into $INSTALL_DIR"

# ------------------------------------------------------------- steam library

if [ ! -f "$INSTALL_DIR/libsteam_api.so" ]; then
    FOUND=""

    for candidate in \
        "$REPO_DIR/libsteam_api.so" \
        "${STEAMWORKS_SDK:-}/redistributable_bin/linux64/libsteam_api.so" \
        "$HOME/steamworks_sdk/redistributable_bin/linux64/libsteam_api.so"
    do
        [ -n "$candidate" ] && [ -f "$candidate" ] && FOUND="$candidate" && break
    done

    if [ -n "$FOUND" ]; then
        cp "$FOUND" "$INSTALL_DIR/libsteam_api.so"
        ok "libsteam_api.so from $FOUND"
    else
        warn "libsteam_api.so is missing — the server cannot start without it"
        echo "      It is a Valve redistributable, so it is not shipped here."
        echo "      Get the Steamworks SDK: https://partner.steamgames.com/downloads/list"
        echo "      Then:  STEAMWORKS_SDK=/path/to/sdk $0"
        echo "      Or copy redistributable_bin/linux64/libsteam_api.so to $INSTALL_DIR/"
    fi
else
    ok "libsteam_api.so"
fi

# ------------------------------------------------------------- config

if [ ! -f "$INSTALL_DIR/server.json" ]; then
    cp "$PROJECT_DIR/server.example.json" "$INSTALL_DIR/server.json"
    ok "created server.json from the example"
else
    ok "kept your existing server.json"
fi

# ------------------------------------------------------------- helper scripts

cat > "$INSTALL_DIR/steam-supervisor.sh" <<SUP
#!/bin/sh
# Steam's launcher forks and returns, so a plain Type=simple unit would see the
# service finish immediately and restart it forever. Start Steam, then block for as
# long as it lives. Exiting non-zero when it dies is what lets Restart=always rebuild
# the stack after a Steam crash, which does happen.
export DISPLAY=${DISPLAY_NUM}

if ! pgrep -x steam >/dev/null; then
    /usr/bin/steam -no-browser >/tmp/steam.log 2>&1 &
fi

for _ in \$(seq 1 90); do
    pgrep -x steam >/dev/null && break
    sleep 1
done

pgrep -x steam >/dev/null || { echo "Steam did not start within 90s"; exit 1; }
echo "Steam is up"

while pgrep -x steam >/dev/null; do
    sleep 5
done

echo "Steam exited"
exit 1
SUP
chmod +x "$INSTALL_DIR/steam-supervisor.sh"

# One-time sign-in helper. Steam Guard needs a human, and on a headless box there is
# no screen to be a human at — so this optionally exposes the virtual display over
# VNC just long enough to log in.
cat > "$INSTALL_DIR/steam-login.sh" <<'LOGIN'
#!/usr/bin/env bash
# Sign in to Steam once. Credentials are cached afterwards and the services
# take over. Run this on the machine that will host the server.
set -euo pipefail

DISPLAY_NUM="${FUSION_DISPLAY:-:99}"

if [ -n "${DISPLAY:-}" ] && [ "${1:-}" != "--headless" ]; then
    echo "Using your existing desktop session. Sign in, then close Steam."
    exec steam -no-browser
fi

echo "No desktop session — starting a virtual display and exposing it over VNC."
command -v x11vnc >/dev/null || {
    echo "x11vnc is required for headless sign-in. Install it and re-run." >&2
    exit 1
}

pgrep -f "Xvfb $DISPLAY_NUM" >/dev/null || {
    rm -f "/tmp/.X${DISPLAY_NUM#:}-lock" "/tmp/.X11-unix/X${DISPLAY_NUM#:}"
    Xvfb "$DISPLAY_NUM" -screen 0 1280x800x24 >/dev/null 2>&1 &
    sleep 2
}

DISPLAY="$DISPLAY_NUM" steam -no-browser >/tmp/steam-login.log 2>&1 &

echo
echo "  From your own machine, tunnel in and connect a VNC viewer to localhost:5900:"
echo "      ssh -L 5900:localhost:5900 $USER@$(hostname -I 2>/dev/null | awk '{print $1}')"
echo
echo "  Sign in, then press Ctrl+C here. Steam remembers you afterwards."
echo

x11vnc -display "$DISPLAY_NUM" -localhost -nopw -forever -quiet
LOGIN
chmod +x "$INSTALL_DIR/steam-login.sh"

# ------------------------------------------------------------- units

say "Writing systemd user units"
mkdir -p "$UNIT_DIR"

cat > "$UNIT_DIR/fusion-xvfb.service" <<UNIT
[Unit]
Description=Fusion Dedicated virtual display
After=default.target

[Service]
Type=simple
# A killed Xvfb leaves its lock behind and refuses to start while it exists.
ExecStartPre=-/bin/rm -f /tmp/.X${DISPLAY_NUM#:}-lock /tmp/.X11-unix/X${DISPLAY_NUM#:}
ExecStart=$(command -v Xvfb) ${DISPLAY_NUM} -screen 0 1280x800x24
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
UNIT

cat > "$UNIT_DIR/fusion-steam.service" <<UNIT
[Unit]
Description=Steam client for Fusion Dedicated
After=fusion-xvfb.service network-online.target
Requires=fusion-xvfb.service
Wants=network-online.target

[Service]
Type=simple
Environment=DISPLAY=${DISPLAY_NUM}
ExecStart=$INSTALL_DIR/steam-supervisor.sh
Restart=always
RestartSec=15
# Steam can assert and die; never stop retrying.
StartLimitIntervalSec=0

[Install]
WantedBy=default.target
UNIT

cat > "$UNIT_DIR/fusion-server.service" <<UNIT
[Unit]
Description=Fusion Dedicated relay server
After=fusion-steam.service
Requires=fusion-steam.service
# Steam dying takes the relay with it, so tie their lifecycles together.
PartOf=fusion-steam.service

[Service]
Type=simple
WorkingDirectory=$INSTALL_DIR
Environment=DISPLAY=${DISPLAY_NUM}
Environment=LD_LIBRARY_PATH=$INSTALL_DIR
ExecStart=$(command -v dotnet) $INSTALL_DIR/fusiondedicated.dll
# Steam takes a while to come up; a failed SteamAPI.Init simply retries.
Restart=always
RestartSec=15
StartLimitIntervalSec=0

[Install]
WantedBy=default.target
UNIT

systemctl --user daemon-reload
ok "fusion-xvfb, fusion-steam, fusion-server"

# ------------------------------------------------------------- lingering

LINGER="$(loginctl show-user "$USER" -p Linger --value 2>/dev/null || echo no)"

if [ "$LINGER" != "yes" ]; then
    warn "Lingering is off — services will stop when you log out"
    echo "      Enable with:  sudo loginctl enable-linger $USER"
else
    ok "lingering enabled (services survive logout and start at boot)"
fi

# ------------------------------------------------------------- done

echo
say "Installed to $INSTALL_DIR"
cat <<DONE

  1. Sign in to Steam once — Steam Guard needs a human:

       $INSTALL_DIR/steam-login.sh

     The account needs SteamVR (app 250820) in its library; it is free.
     Use a separate account: Steam allows one session at a time, so this
     will sign you out of your own games otherwise.

  2. Start everything:

       systemctl --user enable --now fusion-xvfb fusion-steam fusion-server

  3. Watch it come up:

       journalctl --user -u fusion-server -f

  4. Open the panel. It listens on localhost only by default:

       ssh -L 8778:localhost:8778 $USER@$(hostname -I 2>/dev/null | awk '{print $1}')
       then browse to http://localhost:8778

     To expose it on your LAN instead, set "DashboardHost": "+" in
     $INSTALL_DIR/server.json — but read the security note in the README,
     because the panel has no login.

DONE
