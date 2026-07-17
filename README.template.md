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

Docs: https://concordlib.dev
Source and issues: https://github.com/ConcordLib/RimWorld
