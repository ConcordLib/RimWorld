using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch(typeof(DirectXmlLoader))]
public static class DefFromNodePatch {
    [Inject(At.Around, nameof(DirectXmlLoader.DefFromNode))]
    public static Def WrapDefFromNode(XmlNode node, LoadableXmlAsset loadingAsset, ControlHandle<Def> ch) {
        List<KeyValuePair<PropertyEntry, object>> lifted = Strip(node);
        Def result = DirectXmlLoader.DefFromNode(node, loadingAsset);
        Apply(result, lifted);
        return result;
    }

    private static List<KeyValuePair<PropertyEntry, object>> Strip(XmlNode node) {
        PropertyRegistry registry = RimWorldRuntime.Registry;
        if (registry == null || registry.IsEmpty || node == null) {
            return null;
        }

        Type defType = ResolveDefType(node);
        if (defType == null) {
            return null;
        }

        IReadOnlyList<PropertyEntry> entries = registry.ForBaseType(defType);
        if (entries.Count == 0) {
            return null;
        }

        List<KeyValuePair<PropertyEntry, object>> lifted = null;
        foreach (PropertyEntry entry in entries) {
            XmlNode child = FindChild(node, entry.Key);
            if (child == null) {
                continue;
            }

            object value = ParseValue(child, entry.ValueType, entry.Key);
            if (entry.Validate != null && !entry.Validate(value)) {
                throw new ArgumentException("Concord attached property '" + entry.Key + "' failed validation on " + defType.FullName + ".");
            }

            node.RemoveChild(child);
            lifted ??= [];
            lifted.Add(new KeyValuePair<PropertyEntry, object>(entry, value));
        }

        return lifted;
    }

    private static Type ResolveDefType(XmlNode node) {
        string typeName = node.Name;
        return GenTypes.GetTypeInAnyAssembly(typeName, "Verse") ?? GenTypes.GetTypeInAnyAssembly(typeName, "RimWorld");
    }

    private static void Apply(Def result, List<KeyValuePair<PropertyEntry, object>> lifted) {
        if (result == null || lifted == null) {
            return;
        }

        foreach (KeyValuePair<PropertyEntry, object> pair in lifted) {
            pair.Key.Slot.Set(result, pair.Value);
        }
    }

    private static XmlNode FindChild(XmlNode node, string key) {
        foreach (XmlNode child in node.ChildNodes) {
            if (child.NodeType == XmlNodeType.Element && string.Equals(child.Name, key, StringComparison.OrdinalIgnoreCase)) {
                return child;
            }
        }

        return null;
    }

    private static object ParseValue(XmlNode node, Type valueType, string key) {
        if (valueType == typeof(int)) {
            string text = node.InnerText;
            if (text == null || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
                throw new ArgumentException("Concord attached-property '" + key + "' expected an int but got '" + (text ?? "<null>") + "'.");
            }

            return parsed;
        }

        throw new NotSupportedException("Concord attached-property type not yet supported for XML parse: " + valueType.FullName);
    }
}
