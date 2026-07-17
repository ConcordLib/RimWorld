using System;
using System.Collections.Generic;
using System.Reflection;

namespace Concord.RimWorld;

public static class ReflectionHarmonyObserver
{
    public static Func<MethodBase, IReadOnlyList<string>> TryCreateForeignOwnerLookup(
        Func<Assembly[]> loadedAssemblies,
        Action<string> log)
    {
        Assembly harmonyAssembly = null;
        foreach (Assembly assembly in loadedAssemblies())
        {
            if (assembly.GetName().Name == "0Harmony")
            {
                harmonyAssembly = assembly;
                break;
            }
        }

        if (harmonyAssembly == null)
        {
            log("[Concord.RimWorld] ReflectionHarmonyObserver: 0Harmony assembly not found");
            return null;
        }

        Type patchProcessorType = harmonyAssembly.GetType("HarmonyLib.PatchProcessor");
        if (patchProcessorType == null)
        {
            log("[Concord.RimWorld] ReflectionHarmonyObserver: HarmonyLib.PatchProcessor not found");
            return null;
        }

        MethodInfo getPatchInfoMethod = patchProcessorType.GetMethod(
            "GetPatchInfo",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new Type[] { typeof(MethodBase) },
            null);

        if (getPatchInfoMethod == null)
        {
            log("[Concord.RimWorld] ReflectionHarmonyObserver: PatchProcessor.GetPatchInfo not found");
            return null;
        }

        return (MethodBase target) => GetForeignOwners(target, getPatchInfoMethod, log);
    }

    private static IReadOnlyList<string> GetForeignOwners(
        MethodBase target,
        MethodInfo getPatchInfoMethod,
        Action<string> log)
    {
        List<string> owners = new List<string>();

        try
        {
            object patchInfo = getPatchInfoMethod.Invoke(null, new object[] { target });
            if (patchInfo == null)
            {
                return owners.AsReadOnly();
            }

            Type patchesType = patchInfo.GetType();
            string[] memberNames = { "Prefixes", "Postfixes", "Transpilers", "Finalizers", "InnerPrefixes", "InnerPostfixes" };

            foreach (string memberName in memberNames)
            {
                object patchCollection = GetMemberValue(patchInfo, patchesType, memberName, log);
                if (patchCollection == null)
                {
                    continue;
                }

                ExtractOwnersFromCollection(patchCollection, owners, log);
            }
        }
        catch (Exception e)
        {
            log($"[Concord.RimWorld] ReflectionHarmonyObserver: Error getting patch info: {e}");
        }

        return owners.AsReadOnly();
    }

    private static object GetMemberValue(object instance, Type type, string memberName, Action<string> log)
    {
        FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null)
        {
            return field.GetValue(instance);
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null)
        {
            return property.GetValue(instance);
        }

        return null;
    }

    private static void ExtractOwnersFromCollection(object collection, List<string> owners, Action<string> log)
    {
        try
        {
            Type collectionType = collection.GetType();
            PropertyInfo countProperty = collectionType.GetProperty("Count");
            if (countProperty == null)
            {
                return;
            }

            int count = (int)countProperty.GetValue(collection);
            PropertyInfo indexerProperty = collectionType.GetProperty("Item");
            if (indexerProperty == null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                object patch = indexerProperty.GetValue(collection, new object[] { i });
                if (patch == null)
                {
                    continue;
                }

                Type patchType = patch.GetType();
                FieldInfo ownerField = patchType.GetField("owner", BindingFlags.Public | BindingFlags.Instance);
                string owner = null;

                if (ownerField != null)
                {
                    owner = ownerField.GetValue(patch) as string;
                }
                else
                {
                    PropertyInfo ownerProperty = patchType.GetProperty("owner", BindingFlags.Public | BindingFlags.Instance);
                    if (ownerProperty != null)
                    {
                        owner = ownerProperty.GetValue(patch) as string;
                    }
                }

                if (owner != null && !owners.Contains(owner))
                {
                    owners.Add(owner);
                }
            }
        }
        catch (Exception e)
        {
            log($"[Concord.RimWorld] ReflectionHarmonyObserver: Error extracting owners: {e}");
        }
    }
}
