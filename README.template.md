Concord is a runtime patching library for RimWorld mods. Mod authors write [Patch]/[Inject] templates (similar to Java's Mixin library) instead of Harmony-style prefix and postfix methods, and get attached data with save persistence and custom XML def fields on top.

## For players

Load Concord before every mod that depends on it. Put it at the top of your mod list, above the core game. There is nothing to configure.

Mods that depend on Concord will not work without it.

## For mod authors

- Author patches as [Patch] classes that extend the target type, with [Inject] methods at Head, Tail, or Around positions
- Attach save-persisted data to things without touching their classes
- Add custom XML fields to defs, and Concord lifts them before vanilla parsing
- The Concord runtime ships as 0Concord.dll and loads before your mod

### Examples

A patch extends the type it patches, so target members are in scope. Change a return value by injecting at the tail:

```csharp
[Patch]
abstract class ThingInspectPatch : Thing {
    [Inject(At.Tail, nameof(GetInspectString))]
    void AfterGetInspectString(ControlHandle<string> ch) {
        ch.ReturnValue += "\nConcord was here.";
    }
}
```

Cancel a method before it runs. No more zzzt:

```csharp
[Patch]
abstract class ShortCircuitPatch : IncidentWorker_ShortCircuit {
    [Inject(At.Head, nameof(TryExecuteWorker))]
    void BeforeTryExecuteWorker(ControlHandle<bool> ch) {
        ch.ReturnValue = false;
        ch.Cancel();
    }
}
```

TryExecuteWorker is protected. That works because the patch extends the target type, which puts protected members and nameof in reach.

Give every thing its own damage tally:

```csharp
[Patch]
abstract class DamageTallyPatch : Thing {
    static readonly AttachedField<Thing, float> TotalDamage = new();

    [Inject(At.Tail, nameof(TakeDamage))]
    void AfterTakeDamage(DamageInfo dinfo) {
        TotalDamage.Set(this, TotalDamage.Get(this) + dinfo.Amount);
    }
}
```

Apply your patches once, from your mod's constructor:

```csharp
public class MyMod : Mod {
    public MyMod(ModContentPack content) : base(content) {
        Concord.Patcher.Apply(typeof(MyMod).Assembly);
    }
}
```

### Harmony coexistence

You can use Concord and Harmony in the same mod, and most of the time you don't have to think about it. Concord bundles every injection on a method into one patch before Harmony sees it. Harmony treats that as a single patch, so no other mod can slip in between two of your injections. Two things can still bite you. If another mod's Harmony prefix returns `false`, the original method never runs, and neither do your Concord injections. If Harmony patches a method Concord had already patched directly, Concord logs an error and stops injecting there until you sort out the load order or the patch libraries.

Concord only hands patches to Harmony when it finds `0Harmony` inside a mod you have enabled, and it only supports the 2.4.x line. A few patch shapes can't go through Harmony at all, so Concord rejects them up front. Those are `Around` on a constructor, inner and infix patches, filter and fault exception regions, and a few rarer IL cases. Two settings change this. One disables the Harmony handoff, so Concord patches everything directly and just logs the conflicts. The other sends every patched method through Harmony, not only the ones another mod also touched.

Docs: https://concordlib.dev
Source and issues: https://github.com/ConcordLib/RimWorld
