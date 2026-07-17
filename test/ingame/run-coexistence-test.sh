#!/usr/bin/env bash
set -euo pipefail

MODE="${1:---contested}"
case "$MODE" in --contested|--uncontested|--harmony-absent) ;; *) echo "usage: $0 [--contested|--uncontested|--harmony-absent]"; exit 2 ;; esac

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RIMWORLD=/mnt/games/SteamLibrary/steamapps/common/RimWorld
HARMONY_WS=/mnt/games/SteamLibrary/steamapps/workshop/content/294100/2009463077
WORK=$(mktemp -d /tmp/concord-coex-test.XXXX)
LOG="$WORK/player.log"
GAME=""

LINKS=("$RIMWORLD/Mods/coex-harmony" "$RIMWORLD/Mods/coex-concord" "$RIMWORLD/Mods/coex-harmonyhalf" "$RIMWORLD/Mods/coex-concordhalf")
for link in "${LINKS[@]}"; do
  if [ -e "$link" ] || [ -L "$link" ]; then
    echo "REFUSING: $link already exists"
    exit 2
  fi
done

# cleanup removes ONLY links this run created (never a path someone else made after the precheck)
CREATED=()
make_link() {
  ln -s "$1" "$2"
  CREATED+=("$2")
}

cleanup() {
  [ -n "$GAME" ] && kill "$GAME" 2>/dev/null || true
  [ -n "$GAME" ] && wait "$GAME" 2>/dev/null || true
  for link in ${CREATED[@]+"${CREATED[@]}"}; do rm -f "$link"; done
  rm -rf "$WORK"
}
trap cleanup EXIT

# 1. build everything (base + bridge via the solution, then the halves)
dotnet build "$REPO_ROOT/Concord.RimWorld.slnx" --verbosity quiet
dotnet build "$SCRIPT_DIR/HarmonyHalfMod/Source/HarmonyHalf.csproj" --verbosity quiet
dotnet build "$SCRIPT_DIR/ConcordHalfMod/Source/ConcordHalf.csproj" --verbosity quiet

# 2. stage the PINNED net472 runtime as 0Concord.dll (the version under test, not whatever is
#    newest in the cache — read the pin from the Tests csproj, same source of truth as CI)
PINNED=$(sed -n 's/.*Concord.Runtime" Version="\([^"]*\)".*/\1/p' "$REPO_ROOT/Source/ConcordRimWorld.Tests/ConcordRimWorld.Tests.csproj")
test -n "$PINNED" || { echo "FAIL: no Concord.Runtime pin in the Tests csproj"; exit 1; }
RUNTIME="$HOME/.nuget/packages/concord.runtime/$PINNED/lib/net472/Concord.dll"
if [ ! -f "$RUNTIME" ]; then
  dotnet restore "$REPO_ROOT/Concord.RimWorld.slnx" --verbosity quiet
fi
test -f "$RUNTIME" || { echo "FAIL: Concord.Runtime $PINNED has no lib/net472/Concord.dll"; exit 1; }
cp "$RUNTIME" "$REPO_ROOT/Current/Assemblies/0Concord.dll"

# 3. symlinks: the CONCORD mod is the repo root (About/ + LoadFolders.xml live there; Current/ has no About)
make_link "$HARMONY_WS" "$RIMWORLD/Mods/coex-harmony"
make_link "$REPO_ROOT" "$RIMWORLD/Mods/coex-concord"
make_link "$SCRIPT_DIR/HarmonyHalfMod" "$RIMWORLD/Mods/coex-harmonyhalf"
make_link "$SCRIPT_DIR/ConcordHalfMod" "$RIMWORLD/Mods/coex-concordhalf"

# 4. isolated savedata + per-mode load order (Concord declares loadBefore Ludeon.RimWorld:
#    harmony -> concord -> core -> halves)
mkdir -p "$WORK/savedata/Config"
ACTIVE="<li>brrainz.harmony</li><li>concordlib.concord</li><li>ludeon.rimworld</li>"
case "$MODE" in
  --contested)       ACTIVE="$ACTIVE<li>concordtest.harmonyhalf</li><li>concordtest.concordhalf</li>" ;;
  --uncontested)     ACTIVE="$ACTIVE<li>concordtest.concordhalf</li>" ;;
  --harmony-absent)  ACTIVE="<li>concordlib.concord</li><li>ludeon.rimworld</li><li>concordtest.concordhalf</li>" ;;
esac
cat > "$WORK/savedata/Config/ModsConfig.xml" <<XML
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData><version/><activeMods>$ACTIVE</activeMods><knownExpansions/></ModsConfigData>
XML

# 5. launch (-logFile is SPACE-separated; the '=' form is silently ignored)
"$RIMWORLD/RimWorldLinux" -savedatafolder="$WORK/savedata" -logFile "$LOG" -popupwindow &
GAME=$!

# 6. wait for the probe marker (max 4 min; the if-form keeps set -e from killing the loop)
for _ in $(seq 1 240); do
  if grep -q "\[COEX\] invoke-result=" "$LOG" 2>/dev/null; then
    break
  fi
  sleep 1
done
kill "$GAME" 2>/dev/null || true
wait "$GAME" 2>/dev/null || true
GAME=""

# 7. assertions (negative asserts use the if-form: `grep && fail` would trip set -e on the PASS path)
fail() { echo "FAIL($MODE): $1"; echo "--- log tail ---"; tail -40 "$LOG" 2>/dev/null; exit 1; }
require() { grep -q "$1" "$LOG" || fail "$2"; }
forbid()  { if grep -q "$1" "$LOG"; then fail "$2"; fi; }

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
