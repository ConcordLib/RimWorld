# Concord for RimWorld

The RimWorld adapter for [Concord](https://github.com/ConcordLib/Core). It ships the Concord runtime as a RimWorld mod, so any mod can write `[Patch]`/`[Inject]` templates with attached data and save persistence.

Load it before every mod that depends on it. It sets `loadBefore Ludeon.RimWorld`.

## Runtime variant

The mod ships the `net472` variant of `Concord.Runtime` as `Current/Assemblies/0Concord.dll`. That's the only variant whose merged MonoMod resolves `System.Reflection.Emit` from `mscorlib` under RimWorld's Unity Mono. The `netstandard2.0` variant references facade assemblies the game doesn't ship and hard-crashes at load. CI stages the dll from the `Concord.Runtime` version pinned in `Source/ConcordRimWorld.Tests/ConcordRimWorld.Tests.csproj`. When Core cuts a release, bump that pin so the staged variant follows.

The optional Harmony bridge dll ships at `Current/Bridge/ConcordRimWorld.Harmony.dll`, deliberately outside the scanned `Current/Assemblies/` and `1.5/Assemblies/` directories. It is never picked up by RimWorld's mod loader; Concord path-loads it itself, and only when a supported Harmony is present.

## Harmony coexistence

You can use Concord and Harmony in the same mod, and most of the time you don't have to think about it. Concord bundles every injection on a method into one patch before Harmony sees it. Harmony treats that as a single patch, so no other mod can slip in between two of your injections. Two things can still bite you. If another mod's Harmony prefix returns `false`, the original method never runs, and neither do your Concord injections. If Harmony patches a method Concord had already patched directly, Concord logs an error and stops injecting there until you sort out the load order or the patch libraries.

Concord only hands patches to Harmony when it finds `0Harmony` inside a mod you have enabled, and it only supports the 2.4.x line. A few patch shapes can't go through Harmony at all, so Concord rejects them up front. Those are `Around` on a constructor, inner and infix patches, filter and fault exception regions, and a few rarer IL cases. Two settings change this. One disables the Harmony handoff, so Concord patches everything directly and just logs the conflicts. The other sends every patched method through Harmony, not only the ones another mod also touched.
