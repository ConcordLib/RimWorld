# Concord for RimWorld

The RimWorld game adapter for [Concord](https://github.com/ConcordLib/Core). Packages the Concord runtime as a RimWorld mod so any mod can author `[Patch]`/`[Inject]` templates with attached data and save persistence.

Load this before every mod that depends on it (it sets `loadBefore Ludeon.RimWorld`).

## Runtime flavor

The mod ships the `net472` flavor of `Concord.Runtime` as `Current/Assemblies/0Concord.dll` — the only flavor whose merged MonoMod resolves `System.Reflection.Emit` from `mscorlib` under RimWorld's Unity Mono (the `netstandard2.0` flavor references facade assemblies the game does not ship and hard-crashes at load). CI stages it from the `Concord.Runtime` version pinned in `Source/ConcordRimWorld.Tests/ConcordRimWorld.Tests.csproj`; when Core cuts a release, bump that pin so the staged flavor follows.
