using System;
using System.Collections.Generic;
using Concord.AttachedData;

namespace Concord.RimWorld;

public sealed class PropertyRegistry {
    private readonly List<PropertyEntry> entries = [];
    private readonly HashSet<string> keys = [];
    private readonly Dictionary<Type, PropertyEntry[]> byType = [];

    public bool IsEmpty => entries.Count == 0;

    public void Add(Type baseType, string key, Type valueType, Func<object, bool> validate) {
        if (IsBclType(baseType)) {
            throw new ArgumentException("Attached properties cannot target BCL types: " + baseType.FullName, nameof(baseType));
        }

        if (!IsSupportedValueType(valueType)) {
            throw new ArgumentException("Attached-property type is not supported for save/load: " + valueType.FullName, nameof(valueType));
        }

        string composite = baseType.FullName + "::" + key;
        if (!keys.Add(composite)) {
            throw new InvalidOperationException("Duplicate attached property key: " + composite);
        }

        entries.Add(new PropertyEntry(baseType, key, valueType, validate, new Slot(), "concord." + key));
        byType.Clear();
    }

    public IReadOnlyList<PropertyEntry> ForBaseType(Type type) {
        if (byType.TryGetValue(type, out PropertyEntry[] cached)) {
            return cached;
        }

        List<PropertyEntry> result = [];
        foreach (PropertyEntry entry in entries) {
            if (entry.BaseType.IsAssignableFrom(type)) {
                result.Add(entry);
            }
        }

        PropertyEntry[] array = result.ToArray();
        byType[type] = array;
        return array;
    }

    private static bool IsSupportedValueType(Type valueType) {
        return valueType == typeof(int);
    }

    private static bool IsBclType(Type type) {
        string ns = type.Namespace;
        if (ns == null) {
            return false;
        }

        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);
    }

    private sealed class Slot : IAttachedSlot {
        private readonly AttachedField<object, object> field = new AttachedField<object, object>();

        public object Get(object target) {
            return field.Get(target);
        }

        public void Set(object target, object value) {
            field.Set(target, value);
        }
    }
}
