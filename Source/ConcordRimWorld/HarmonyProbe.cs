using System;
using System.IO;
using System.Reflection;

namespace Concord.RimWorld;

public static class HarmonyProbe
{
    internal const string BridgeRelativePath = "Current/Bridge/ConcordRimWorld.Harmony.dll";

    public static bool HarmonyPresent(Func<Assembly[]> loadedAssemblies)
    {
        Assembly[] assemblies = loadedAssemblies();
        return Array.Exists(assemblies, a => a.GetName().Name == "0Harmony");
    }

    public static IHarmonyBridge TryLoadBridge(string modRootDir, Action<string> log)
    {
        return TryLoadBridge(modRootDir, log, () => AppDomain.CurrentDomain.GetAssemblies());
    }

    internal static IHarmonyBridge TryLoadBridge(string modRootDir, Action<string> log, Func<Assembly[]> loadedAssemblies)
    {
        if (!HarmonyPresent(loadedAssemblies))
        {
            log("Harmony not present; bridge cannot be loaded.");
            return null;
        }

        string bridgePath = Path.Combine(modRootDir, "Current", "Bridge", "ConcordRimWorld.Harmony.dll");

        if (!File.Exists(bridgePath))
        {
            log($"Bridge file not found at {bridgePath}");
            return null;
        }

        try
        {
            Assembly bridgeAssembly = Assembly.LoadFrom(bridgePath);
            Assembly harmonyAssembly = Array.Find(
                loadedAssemblies(),
                a => a.GetName().Name == "0Harmony"
            );

            Version harmonyVersion = harmonyAssembly.GetName().Version;

            if (!VersionSupported(harmonyVersion, log))
            {
                return null;
            }

            Type bridgeType = null;
            foreach (Type type in bridgeAssembly.GetTypes())
            {
                if (typeof(IHarmonyBridge).IsAssignableFrom(type) && !type.IsInterface)
                {
                    bridgeType = type;
                    break;
                }
            }

            if (bridgeType == null)
            {
                log("No IHarmonyBridge implementation found in bridge assembly.");
                return null;
            }

            IHarmonyBridge bridge = (IHarmonyBridge)Activator.CreateInstance(bridgeType, new object[] { log });

            log($"{CoexistenceLogMarkers.BridgeActive} {harmonyVersion}");

            return bridge;
        }
        catch (Exception ex)
        {
            log($"Exception loading bridge: {ex.Message}");
            return null;
        }
    }

    internal static bool VersionSupported(Version found, Action<string> log)
    {
        if (found >= new Version(2, 4, 1) && found < new Version(2, 5, 0))
        {
            return true;
        }

        log($"Harmony version {found} not supported; bridge requires [2.4.1, 2.5).");
        return false;
    }
}
