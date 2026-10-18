#!/usr/bin/env bash
set -euo pipefail

core_dir="$1"
tfm="$2"
dest_dir="$3"
min_bytes=1000000

lock_dir="${XDG_RUNTIME_DIR:-/tmp}/concord-stage"
mkdir -p "$lock_dir"
exec 9>"$lock_dir/$(printf '%s' "$(cd "$core_dir" && pwd -P)" | md5sum | cut -d' ' -f1).lock"
flock 9

dotnet build "$core_dir/Concord.slnx" -c Release --verbosity quiet

core_dll="$core_dir/Assemblies/$tfm/Concord.dll"
harmony_dll="$core_dir/src/Concord.Harmony/bin/Release/$tfm/Concord.Harmony.dll"

[[ -f "$core_dll" ]] || { echo "Core built but produced no $core_dll" >&2; exit 1; }
[[ -f "$harmony_dll" ]] || { echo "Core built but produced no $harmony_dll" >&2; exit 1; }

size=$(stat -c%s "$core_dll")
if (( size < min_bytes )); then
  echo "Concord.dll at $core_dll is only $size bytes; expected the ILRepack-merged runtime at roughly 2.8MB (>= $min_bytes bytes). A pre-merge or mid-ILRepack artifact got staged instead." >&2
  exit 1
fi

mkdir -p "$dest_dir/Assemblies" "$dest_dir/Bridge"
cp "$core_dll" "$dest_dir/Assemblies/0Concord.dll"
cp "$harmony_dll" "$dest_dir/Bridge/Concord.Harmony.dll"
