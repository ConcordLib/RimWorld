using System;
using System.Collections.Generic;
using System.Linq;
using Concord.AttachedData;
using Verse;

namespace Concord.RimWorld;

public sealed class PropertyRegistry {
    private readonly List<PropertyEntry> entries = [];
    private readonly HashSet<string> keys = [];
    private readonly Dictionary<Type, PropertyEntry[]> byType = [];

    public bool IsEmpty => entries.Count == 0;

    public void Add(Type baseType, string key, Type valueType, Func<object, bool> validate) {
        Add(baseType, key, valueType, validate, new Slot());
    }

    public void Add(Type baseType, string key, Type valueType, Func<object, bool> validate, IAttachedSlot slot) {
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

        // The label carries the target type too. Without it one assembly declaring the same field name on
        // two targets writes two elements of the same name, and the loader hands the first to both.
        string label = "concord." + Label(baseType) + "." + key;
        if (!IsXmlName(label)) {
            throw new ArgumentException("Attached-property name is not usable as a save label: " + label, nameof(key));
        }

        entries.Add(new PropertyEntry(baseType, key, valueType, validate, slot, label, Scribers.For(valueType, slot, label)));
        byType.Clear();
    }

    public IReadOnlyList<PropertyEntry> ForBaseType(Type type) {
        if (byType.TryGetValue(type, out PropertyEntry[] cached)) {
            return cached;
        }

        PropertyEntry[] array = entries.Where(entry => entry.BaseType.IsAssignableFrom(type)).ToArray();
        byType[type] = array;
        return array;
    }

    private static string Label(Type baseType) {
        return baseType.FullName.Replace('+', '.');
    }

    private static bool IsXmlName(string label) {
        for (int i = 0; i < label.Length; i++) {
            char c = label[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-') {
                return false;
            }
        }

        return label.Length > 0 && (char.IsLetter(label[0]) || label[0] == '_');
    }

    private static bool IsSupportedValueType(Type valueType) {
        return valueType.IsEnum || ParseHelper.HandlesType(valueType);
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
