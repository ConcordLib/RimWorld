# Concord for RimWorld

The RimWorld game adapter for [Concord](https://github.com/ConcordLib/Core). Packages the Concord runtime as a RimWorld mod so any mod can author `[Patch]`/`[Inject]` templates with attached data and save persistence.

Load this before every mod that depends on it (it sets `loadBefore Ludeon.RimWorld`).
