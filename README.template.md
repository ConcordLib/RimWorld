Concord is a runtime patching library for RimWorld mods. Mod authors write [Patch]/[Inject] templates (similar to Java's Mixin library) instead of Harmony-style prefix and postfix methods, and get attached data with save persistence and custom XML def fields on top.

## For players

Load Concord before every mod that depends on it. Put it at the top of your mod list, above the core game. There is nothing to configure.

Mods that depend on Concord will not work without it.

## For mod authors

- Author patches as [Patch] classes that extend the target type, with [Inject] methods at Head, Tail, or Around positions
- Attach save-persisted data to things without touching their classes
- Add custom XML fields to defs, lifted before vanilla parsing
- The Concord runtime ships as 0Concord.dll and loads before your mod
