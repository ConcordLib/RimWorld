# Concord for RimWorld

The RimWorld adapter for [Concord](https://github.com/ConcordLib/Core). It ships the Concord runtime as a RimWorld mod, so any mod can write `[Patch]`/`[Inject]` templates with attached data and save persistence.

Load it before every mod that depends on it. It sets `loadBefore Ludeon.RimWorld`.

## Runtime variant

The mod ships the `net472` variant of `Concord.Runtime` as `Current/Assemblies/0Concord.dll`. That's the only variant whose merged MonoMod resolves `System.Reflection.Emit` from `mscorlib` under RimWorld's Unity Mono. The `netstandard2.0` variant references facade assemblies the game doesn't ship and hard-crashes at load. CI stages the dll from the `Concord.Runtime` version pinned in `Source/ConcordRimWorld.Tests/ConcordRimWorld.Tests.csproj`. When Core cuts a release, bump that pin so the staged variant follows.

The optional Harmony bridge dll ships at `Current/Bridge/ConcordRimWorld.Harmony.dll`, deliberately outside the scanned `Current/Assemblies/` and `1.5/Assemblies/` directories. It is never picked up by RimWorld's mod loader; Concord path-loads it itself, and only when a supported Harmony is present.

## Harmony coexistence

On RimWorld, Concord coexists with Harmony patches that exist when the Concord patch is applied. If Harmony later patches a method Concord raw-detoured, Concord detects it and reports the conflict loudly; that method's Concord injections stop applying until load order or patch libs are reconciled.

Most Concord patches apply on a deferred schedule, after every mod constructor and static constructor has run, so the bridge sees the fullest possible picture of what Harmony has already patched before Concord composes its own wrapper. A small set of load-machinery patches (the def-loading `Around` that lifts Concord's attached XML fields) can't wait that long and opt into an eager scope instead, applying immediately during mod construction.

A foreign prefix that skips the original method skips every Concord injection on that method too. That's standard Harmony semantics, not something Concord works around: if a prefix returns false, nothing downstream of it runs, Concord's wrapper included.

Concord is one aggregate participant in Harmony's patch pipeline, not several. Every Concord injection on a given method composes into a single wrapper before Harmony sees it, so foreign patches cannot interleave between two Concord injections on the same method - Concord's internal ordering is preserved as a unit. Foreign transpilers are ordered after Concord's own, and Harmony's internal passes run on Concord's output, not its input.

A few patch shapes are outside what the bridge can route through Harmony and are rejected outright: constructor whole-method `Around` injections, inner/infix patches, filter/fault exception regions, unresolvable operands, argument slots past 255, shared reference-type generic instantiations, and injection IL that calls `GetExecutingAssembly`.

Foreign inner patches added to a target after Concord has already routed it, with no further Concord application on that method afterward, aren't detectable at compose time. That's watchdog territory, caught (if at all) by the periodic contention checks rather than at patch time.

When the injection set on a contested target changes, Concord has to unpatch and repatch the whole composed wrapper, which opens a brief call-time gap where the target runs unpatched.

Mono can inline small methods before any patch library gets a chance to detour them. Methods used as coexistence test or probe targets should carry `[MethodImpl(MethodImplOptions.NoInlining)]` to keep that from producing false negatives.

The supported Harmony line is 2.4.x - the only line validated against a real Harmony binary. That gate only widens when a new binary has actually been validated against it, not on a version-number guess.

Two settings control this behavior: a kill switch that disables bridge activation outright (the router still installs and still reports raw-pin conflicts, it just never hands routing to Harmony), and a compat mode that routes every method through the bridge when Harmony is present, not just contested ones.
