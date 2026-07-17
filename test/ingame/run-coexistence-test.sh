#!/usr/bin/env bash
set -euo pipefail

MODE="${1:---contested}"
case "$MODE" in --contested|--uncontested|--harmony-absent) ;; *) echo "usage: $0 [--contested|--uncontested|--harmony-absent]"; exit 2 ;; esac

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RIMWORLD=/mnt/games/SteamLibrary/steamapps/common/RimWorld
HARMONY_WS=/mnt/games/SteamLibrary/steamapps/workshop/content/294100/2009463077
CONCORD_WS=/mnt/games/SteamLibrary/steamapps/workshop/content/294100/3758333473
CONFIG_DIR="$HOME/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Config"
MODSCONFIG="$CONFIG_DIR/ModsConfig.xml"
PLAYERLOG="$HOME/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log"
WORK=$(mktemp -d /tmp/concord-coex-test.XXXX)
LOG="$WORK/player.log"
GAME=""

# --- refuse to run over pre-existing coex-* symlinks (someone else's) ---
LINKS=("$RIMWORLD/Mods/coex-harmony" "$RIMWORLD/Mods/coex-concord" "$RIMWORLD/Mods/coex-harmonyhalf" "$RIMWORLD/Mods/coex-concordhalf")
for link in "${LINKS[@]}"; do
  if [ -e "$link" ] || [ -L "$link" ]; then echo "REFUSING: $link already exists"; exit 2; fi
done

# --- restore state on ANY exit (set BEFORE any mutation) ---
CREATED=()
MODSCONFIG_BACKED_UP=0
WS_CONCORD_SIDELINED=0
SIDELINED_DIRS=()
make_link() { ln -s "$1" "$2"; CREATED+=("$2"); }
cleanup() {
  [ -n "$GAME" ] && kill "$GAME" 2>/dev/null || true
  [ -n "$GAME" ] && wait "$GAME" 2>/dev/null || true
  for link in ${CREATED[@]+"${CREATED[@]}"}; do rm -f "$link"; done
  if [ "$MODSCONFIG_BACKED_UP" = "1" ] && [ -f "$WORK/ModsConfig.xml.real" ]; then
    cp -f "$WORK/ModsConfig.xml.real" "$MODSCONFIG"
  fi
  if [ "$WS_CONCORD_SIDELINED" = "1" ] && [ -f "$CONCORD_WS/About/About.xml.coexbak" ]; then
    mv -f "$CONCORD_WS/About/About.xml.coexbak" "$CONCORD_WS/About/About.xml"
  fi
  for f in ${SIDELINED_DIRS[@]+"${SIDELINED_DIRS[@]}"}; do
    [ -e "$f.coexbak" ] && mv -f "$f.coexbak" "$f"
  done
  rm -rf "$WORK"
}
trap cleanup EXIT

# --- 1. build (base+bridge via solution, then halves), stage the pinned runtime as 0Concord.dll ---
dotnet build "$REPO_ROOT/Concord.RimWorld.slnx" -p:RimWorldManaged="$RIMWORLD/RimWorldLinux_Data/Managed" --verbosity quiet
dotnet build "$SCRIPT_DIR/HarmonyHalfMod/Source/HarmonyHalf.csproj" -p:RimWorldManaged="$RIMWORLD/RimWorldLinux_Data/Managed" --verbosity quiet
dotnet build "$SCRIPT_DIR/ConcordHalfMod/Source/ConcordHalf.csproj" -p:RimWorldManaged="$RIMWORLD/RimWorldLinux_Data/Managed" --verbosity quiet
PINNED=$(sed -n 's/.*Concord.Runtime" Version="\([^"]*\)".*/\1/p' "$REPO_ROOT/Source/ConcordRimWorld.Tests/ConcordRimWorld.Tests.csproj")
RUNTIME="$HOME/.nuget/packages/concord.runtime/$PINNED/lib/net472/Concord.dll"
test -f "$RUNTIME" || { echo "FAIL: Concord.Runtime $PINNED net472 dll not in nuget cache"; exit 1; }
cp -f "$RUNTIME" "$REPO_ROOT/Current/Assemblies/0Concord.dll"

# --- 2. sideline the workshop Concord so the branch build is the sole concordlib.concord ---
if [ -f "$CONCORD_WS/About/About.xml" ]; then
  mv "$CONCORD_WS/About/About.xml" "$CONCORD_WS/About/About.xml.coexbak"
  WS_CONCORD_SIDELINED=1
fi

# --- 2b. sideline every OTHER concordlib.concord in install Mods so the branch is the sole one.
#         RimWorld scans every subdir of Mods/ and reads its About.xml regardless of the dir name,
#         so we sideline the About.xml FILE (resolving symlinks to the real file), not the dir. ---
for about in "$RIMWORLD/Mods"/*/About/About.xml; do
  [ -f "$about" ] || continue
  OWNID="$(grep -oiE "<packageId>[^<]+</packageId>" "$about" | head -1 | sed -E 's#</?packageId>##gi' | tr 'A-Z' 'a-z' | tr -d '[:space:]')"
  [ "$OWNID" = "concordlib.concord" ] || continue
  real="$(readlink -f "$about")"
  mv "$real" "$real.coexbak"
  SIDELINED_DIRS+=("$real")
done

# --- 3. symlink the branch mod (repo root) + halves + harmony into the install Mods dir ---
make_link "$HARMONY_WS" "$RIMWORLD/Mods/coex-harmony"
make_link "$REPO_ROOT" "$RIMWORLD/Mods/coex-concord"
make_link "$SCRIPT_DIR/HarmonyHalfMod" "$RIMWORLD/Mods/coex-harmonyhalf"
make_link "$SCRIPT_DIR/ConcordHalfMod" "$RIMWORLD/Mods/coex-concordhalf"

# --- 4. back up + write the real ModsConfig with the per-mode active set (Concord loadBefore core) ---
mkdir -p "$CONFIG_DIR"
if [ -f "$MODSCONFIG" ]; then cp -f "$MODSCONFIG" "$WORK/ModsConfig.xml.real"; MODSCONFIG_BACKED_UP=1; fi
case "$MODE" in
  --contested)      ACTIVE="<li>brrainz.harmony</li><li>concordlib.concord</li><li>ludeon.rimworld</li><li>concordtest.harmonyhalf</li><li>concordtest.concordhalf</li>" ;;
  --uncontested)    ACTIVE="<li>brrainz.harmony</li><li>concordlib.concord</li><li>ludeon.rimworld</li><li>concordtest.concordhalf</li>" ;;
  --harmony-absent) ACTIVE="<li>concordlib.concord</li><li>ludeon.rimworld</li><li>concordtest.concordhalf</li>" ;;
esac
cat > "$MODSCONFIG" <<XML
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData><version>1.6.4871</version><activeMods>$ACTIVE</activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>
XML

# --- 5. launch (space-form -logFile works; -savedatafolder does NOT isolate on 1.6 so we don't use it) ---
: > "$LOG"
"$RIMWORLD/RimWorldLinux" -logFile "$LOG" -popupwindow >/dev/null 2>&1 &
GAME=$!

# --- 6. wait for the probe marker (max 4 min); fall back to the default Player.log if -logFile didn't take ---
FOUND_LOG="$LOG"
for _ in $(seq 1 240); do
  if grep -q "\[COEX\] invoke-result=" "$LOG" 2>/dev/null; then FOUND_LOG="$LOG"; break; fi
  if grep -q "\[COEX\] invoke-result=" "$PLAYERLOG" 2>/dev/null; then FOUND_LOG="$PLAYERLOG"; break; fi
  if ! kill -0 "$GAME" 2>/dev/null; then break; fi
  sleep 1
done
kill "$GAME" 2>/dev/null || true
wait "$GAME" 2>/dev/null || true
GAME=""
# pick whichever log actually has content
[ -s "$LOG" ] || FOUND_LOG="$PLAYERLOG"

# --- 7. assertions (if-form so negative greps don't trip set -e) ---
fail() { echo "FAIL($MODE): $1"; echo "--- log tail ($FOUND_LOG) ---"; grep -iE "\[COEX\]|\[Concord.Coex\]|Concord runtime wired|Exception|error" "$FOUND_LOG" 2>/dev/null | tail -30; exit 1; }
require() { grep -q "$1" "$FOUND_LOG" || fail "$2"; }
forbid()  { if grep -q "$1" "$FOUND_LOG"; then fail "$2"; fi; }

require "\[COEX\] invoke-result=42 head-count=1" "wrong composed result or head count"
require "\[COEX\] concord-head" "concord head missing"
require "\[Concord.Coex\] flush-complete" "deferred flush never ran"
forbid  "\[Concord.Coex\] late-contention" "late-contention diagnostic fired"

case "$MODE" in
  --contested)
    require "\[COEX\] harmony-postfix" "harmony postfix missing"
    require "\[Concord.Coex\] routed-contested" "bridge did not route the contested target"
    ;;
  --uncontested)
    require "\[Concord.Coex\] bridge-active" "bridge should activate when Harmony is present"
    forbid  "\[Concord.Coex\] routed-contested" "uncontested target must stay raw"
    ;;
  --harmony-absent)
    forbid  "\[Concord.Coex\] bridge-active" "bridge must never load without Harmony"
    forbid  "\[Concord.Coex\] routed-contested" "nothing can route without Harmony"
    ;;
esac

echo "PASS($MODE): coexistence holds in-game"
