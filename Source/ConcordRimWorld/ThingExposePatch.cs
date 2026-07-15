using System;
using System.Collections.Generic;
using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch]
public abstract class ThingExposePatch : ThingWithComps {
    [Inject(At.Return, nameof(ExposeData))]
    public void ScribeAttachedProperties(ControlHandle ch) {
        PropertyRegistry registry = RimWorldRuntime.Registry;
        if (registry == null || registry.IsEmpty) {
            return;
        }

        IReadOnlyList<PropertyEntry> entries = registry.ForBaseType(GetType());
        foreach (PropertyEntry entry in entries) {
            ScribeOne(this, entry);
        }
    }

    private static void ScribeOne(ThingWithComps self, PropertyEntry entry) {
        try {
            if (entry.ValueType == typeof(int)) {
                object current = entry.Slot.Get(self);
                int value = current is int i ? i : 0;
                Scribe_Values.Look(ref value, entry.ScribeLabel, 0, false);
                if (Scribe.mode == LoadSaveMode.LoadingVars) {
                    entry.Slot.Set(self, value);
                }

                return;
            }
        } catch (Exception e) {
            Log.Error("[Concord.RimWorld] Failed to scribe attached property '" + entry.Key + "' on " + self.GetType().FullName + ": " + e);
        }
    }
}
