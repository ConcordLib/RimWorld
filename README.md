# Concord for RimWorld

The RimWorld adapter for [Concord](https://github.com/ConcordLib/Core). It ships the Concord runtime as a RimWorld mod, so any mod can write `[Patch]`/`[Inject]` templates with attached data and save persistence.

Load it before every mod that depends on it. It sets `loadBefore Ludeon.RimWorld`.

## Runtime variant

The mod ships the `net472` variant of `Concord.Runtime` as `Current/Assemblies/0Concord.dll`. That's the only variant whose merged MonoMod resolves `System.Reflection.Emit` from `mscorlib` under RimWorld's Unity Mono. The `netstandard2.0` variant references facade assemblies the game doesn't ship and hard-crashes at load. CI stages the dll from the `Concord.Runtime` version pinned in `Source/ConcordRimWorld.Tests/ConcordRimWorld.Tests.csproj`. When Core cuts a release, bump that pin so the staged variant follows.

The optional Harmony bridge dll ships at `Current/Bridge/ConcordRimWorld.Harmony.dll`, deliberately outside the scanned `Current/Assemblies/` and `1.5/Assemblies/` directories. It is never picked up by RimWorld's mod loader; Concord path-loads it itself, and only when a supported Harmony is present.
