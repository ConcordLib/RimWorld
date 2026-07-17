# In-game coexistence test harness

Repeatable, real-game proof that Concord and Harmony coexist correctly on a shared patch
target. Not part of the mod package (`test/` is excluded via `.steamignore`); this only runs
against a local, installed copy of RimWorld.

## What it proves

Two half-mods share one target method, `Concord.RimWorld.CoexTest.SharedTarget.Compute`:

- **ConcordHalfMod** declares the target and a Concord `[Patch]`/`[Inject(At.Head, ...)]` on it,
  applied the way a normal consumer mod applies patches (`Patcher.Apply` from the mod ctor). It
  also schedules a double-deferred `LongEventHandler.ExecuteWhenFinished` callback that invokes
  `Compute(41)` after Concord's own deferred flush has run, logging the result.
- **HarmonyHalfMod** declares a `[StaticConstructorOnStartup]` Harmony postfix on the same
  target, resolved through the AppDomain by name (`Type.GetType("...SharedTarget, CoexConcordHalf")`)
  so it has no compile-time dependency on ConcordHalfMod.

`run-coexistence-test.sh` launches the installed game three times, once per mode, each in an
isolated `-savedatafolder`, and greps the resulting player log for markers proving specific
behavior:

- **`--contested`** — both half-mods active alongside Harmony. Proves the bridge routes a
  target patched by both a Concord injection and a foreign Harmony patch end-to-end: the
  Concord head injection runs, the Harmony postfix runs, and the bridge logs
  `[Concord.Coex] routed-contested` for the shared target.
- **`--uncontested`** — only ConcordHalfMod active, with Harmony present but never touching the
  shared target. Proves the bridge activates (`[Concord.Coex] bridge-active`) whenever Harmony
  is loaded, but a target nothing else patches stays on the raw (non-bridge) path — no
  `routed-contested` for it.
- **`--harmony-absent`** — only ConcordHalfMod active, Harmony not loaded at all. Proves the
  bridge never activates without Harmony present (no `bridge-active`, no `routed-contested`,
  zero Harmony execution) and Concord's own patching still works unassisted.

All three modes additionally assert the composed result is correct (`Compute(41)` returns `42`
with exactly one Concord head firing), the deferred flush completes
(`[Concord.Coex] flush-complete`), and no `[Concord.Coex] late-contention` diagnostic fires.

Two further modes prove what happens when a *third* mod applies its own Harmony patch to the
same target *after* Concord has already routed it through the bridge (i.e. after
`flush-complete` and after the contested baseline in `--contested` has already been proven):

- **`--second-harmony`** (`HarmonySecondMod`) — at a defer level after ConcordHalf's own
  double-deferred probe, applies a second, ordinary Harmony postfix to the shared target, then
  invokes `Compute(41)` again one defer level later. Any `Harmony.Patch` call forces Harmony to
  rebuild the method, which re-runs Concord's lowest-priority transpiler against the fresh
  instruction stream. Because a postfix is a composable construct, Concord's transpiler
  recomposes cleanly: `concord-head` fires *again* on the post-rebuild method (head-count goes
  from 1 to 2), and both the first and second Harmony postfixes still run.
- **`--second-harmony-inner`** (`HarmonyBreakerMod`) — same defer shape, but instead of a
  postfix it installs a Harmony *transpiler* that marks the method body with a `BeginFaultBlock`
  exception region — a construct Concord's converter explicitly rejects
  (`CodeInstructionConverter` throws `NeutralConversionException` on `BeginExceptFilterBlock`/
  `BeginFaultBlock`). On rebuild, Concord's transpiler catches that exception, logs
  `[Concord.Coex] stream-rejected`, and passes Harmony's stream through unmodified. The method
  still returns the correct value and the *other* mod's Harmony postfix (`HarmonyHalfMod`)
  keeps working, but Concord's own head injection stops running on that method — head-count
  stays at 1 instead of advancing to 2. This is a graceful degrade, not a crash: Concord notices
  it can't safely recompose the new stream and steps aside rather than emitting invalid IL.

## Running it

```bash
./test/ingame/run-coexistence-test.sh --contested
./test/ingame/run-coexistence-test.sh --uncontested
./test/ingame/run-coexistence-test.sh --harmony-absent
./test/ingame/run-coexistence-test.sh --second-harmony
./test/ingame/run-coexistence-test.sh --second-harmony-inner
```

Pass `--keep-log` as a second argument to copy the captured player log to
`/tmp/concord-coex-<mode>.log` before the harness deletes its scratch work directory (the
harness always deletes `$WORK` on exit otherwise).

Each run builds the solution and both half-mods, stages the pinned `Concord.Runtime` net472
build as `Current/Assemblies/0Concord.dll`, symlinks the workshop Harmony mod / repo root /
half-mods into the installed RimWorld's `Mods/` folder under fixed `coex-*` names, launches the
game against a fresh temp `-savedatafolder`, and waits (up to 4 minutes) for the probe's
`[COEX] invoke-result=` marker to appear in the log before killing the game and asserting on the
captured log.

Expected runtime is roughly 2-4 minutes per mode, dominated by game boot and mod load, not the
build steps.

The script exits `0` and prints `PASS(<mode>): coexistence holds in-game` when everything checks
out, or exits `1` with a log tail and the specific failed assertion. It **refuses to run** if any
of the `coex-harmony`, `coex-concord`, `coex-harmonyhalf`, `coex-concordhalf`,
`coex-harmonysecond`, or `coex-harmonybreaker` symlinks already exist under the installed
RimWorld's `Mods/` folder (exit `2`) — this guards against clobbering a manually-set-up mod list
or a previous run's links that were never cleaned up. On both normal exit and failure, the
script removes only the symlinks it created itself and the temp savedata directory; it never
touches a `coex-*` path it didn't create in that run.
