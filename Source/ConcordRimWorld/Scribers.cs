using System;
using System.Reflection;
using Concord.AttachedData;
using Verse;

namespace Concord.RimWorld;

internal static class Scribers {
    private static readonly MethodInfo BuildMethod = typeof(Scribers).GetMethod(nameof(Build), BindingFlags.NonPublic | BindingFlags.Static);

    internal static Action<object> For(Type valueType, IAttachedSlot slot, string label) {
        return (Action<object>)BuildMethod.MakeGenericMethod(valueType).Invoke(null, new object[] { slot, label });
    }

    private static Action<object> Build<T>(IAttachedSlot slot, string label) {
        return target => {
            object current = slot.Get(target);
            T value = current is T stored ? stored : default(T);
            Scribe_Values.Look(ref value, label, default(T), false);
            if (Scribe.mode == LoadSaveMode.LoadingVars) {
                slot.Set(target, value);
            }
        };
    }
}
