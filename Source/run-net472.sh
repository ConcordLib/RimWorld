#!/usr/bin/env bash
# Run the net472 test assembly under mono, the same way CI does.
# `dotnet test` cannot: the Linux SDK ships no testhost.net472.exe.
#
#   ./Source/run-net472.sh                # whole suite
#   ./Source/run-net472.sh AdapterWiring  # only classes matching this name
#   CONFIG=Release ./Source/run-net472.sh
set -euo pipefail

CONFIG="${CONFIG:-Debug}"
cd "$(dirname "$0")"

command -v mono >/dev/null || { echo "mono is not installed" >&2; exit 1; }

dotnet build ConcordRimWorld.Tests -c "$CONFIG" >/dev/null

RUNNER=$(find ~/.nuget/packages/xunit.runner.console -name xunit.console.exe -path '*net472*' | sort -V | tail -1)
[[ -n "$RUNNER" ]] || { echo "xunit.runner.console not restored" >&2; exit 1; }

args=()
[[ -n "${1:-}" ]] && args=(-class "Concord.RimWorld.Tests.$1")

cd "ConcordRimWorld.Tests/bin/$CONFIG"
mono "$RUNNER" ConcordRimWorld.Tests.dll -parallel none "${args[@]}"
