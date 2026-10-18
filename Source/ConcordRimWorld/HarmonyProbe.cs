using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Concord.Detour;
using Verse;

namespace Concord.RimWorld;

public static class HarmonyProbe {
#if RW_1_7
  internal const string VersionDir = "1.7";
#elif RW_1_6
  internal const string VersionDir = "1.6";
#elif RW_1_5
  internal const string VersionDir = "1.5";
#elif RW_1_4
  internal const string VersionDir = "1.4";
#elif RW_1_3
  internal const string VersionDir = "1.3";
#elif RW_1_2
  internal const string VersionDir = "1.2";
#elif RW_1_1
  internal const string VersionDir = "1.1";
#else
#error GameVersion has no VersionDir
#endif

  internal const string BridgeFileName = "Concord.Harmony.dll";

  internal static readonly Version supportedFrom = new Version(2, 2, 2);
  internal static readonly Version supportedBefore = new Version(4, 0, 0);

  // Assembly.GetName() computes CodeBase, and mono asserts on that for a byte-loaded assembly.
  // FullName is already built, so it is safe to read on any assembly in the domain.
  internal static string SimpleName(Assembly assembly) {
    string full = assembly.FullName;
    int comma = full.IndexOf(',');
    return comma < 0 ? full : full.Substring(0, comma);
  }

  internal static Version VersionOf(Assembly assembly) {
    string full = assembly.FullName;

    if (full == null) {
      return null;
    }

    const string marker = "Version=";
    int start = full.IndexOf(marker, StringComparison.Ordinal);

    if (start < 0) {
      return null;
    }

    start += marker.Length;
    int end = full.IndexOf(',', start);
    string value = end < 0 ? full.Substring(start) : full.Substring(start, end - start);

    return Version.TryParse(value, out Version parsed) ? parsed : null;
  }

  public static bool HarmonyPresent(Func<Assembly[]> loadedAssemblies) {
    return FindActiveHarmony(loadedAssemblies, ActiveModRoots, _ => { }) != null;
  }

  // A disabled mod's assemblies never load, so an in-AppDomain 0Harmony that sits outside every
  // active mod's folder is a stale or side-loaded copy nothing is patching through.
  internal static Assembly FindActiveHarmony(
      Func<Assembly[]> loadedAssemblies,
      Func<IReadOnlyList<string>> activeModRoots,
      Action<string> log) {
    Assembly[] candidates = Array.FindAll(loadedAssemblies(), a => SimpleName(a) == "0Harmony");
    Assembly harmony = candidates.Length > 0 ? candidates[0] : null;

    if (harmony == null) {
      return null;
    }

    if (candidates.Length > 1) {
      List<string> described = new List<string>();
      foreach (Assembly candidate in candidates) {
        described.Add("#" + candidate.GetHashCode() + " " + (Location(candidate) ?? "<memory>"));
      }

      log("0Harmony is loaded " + candidates.Length + " times: " + string.Join(", ", described) + ". Using #" + harmony.GetHashCode() + ".");
    }

    string location = Location(harmony);
    IReadOnlyList<string> roots = activeModRoots();

    if (location == null) {
      string shipper = FindHarmonyShipper(roots);
      if (shipper != null) {
        log($"0Harmony is loaded from memory (an assembly reloader such as Prepatcher does this); {shipper} ships it, so coexistence stays on.");
        return harmony;
      }

      log("0Harmony is loaded from memory and no enabled mod ships it, so it cannot be traced to an enabled mod; coexistence stays off.");
      return null;
    }


    for (int i = 0; i < roots.Count; i++) {
      if (IsUnder(location, roots[i])) {
        return harmony;
      }
    }

    log($"0Harmony at {location} belongs to no enabled mod (installed but not enabled); coexistence stays off.");
    return null;
  }

  public static IForeignPatchHost TryLoadBridge(string modRootDir, Action<string> log) {
    return TryLoadBridge(modRootDir, log, () => AppDomain.CurrentDomain.GetAssemblies(), ActiveModRoots);
  }

  internal static IForeignPatchHost TryLoadBridge(
      string modRootDir,
      Action<string> log,
      Func<Assembly[]> loadedAssemblies,
      Func<IReadOnlyList<string>> activeModRoots) {
    Assembly harmonyAssembly = FindActiveHarmony(loadedAssemblies, activeModRoots, log);

    if (harmonyAssembly == null) {
      log("Harmony not present; bridge cannot be loaded.");
      return null;
    }

    string bridgePath = Path.Combine(modRootDir, VersionDir, "Bridge", BridgeFileName);

    if (!File.Exists(bridgePath)) {
      log($"Bridge file not found at {bridgePath}");
      return null;
    }

    try {
      Version harmonyVersion = VersionOf(harmonyAssembly);

      if (!VersionSupported(harmonyVersion, log)) {
        return null;
      }

      Assembly bridgeAssembly = Assembly.Load(AssemblyImage.WithFreshModuleId(File.ReadAllBytes(bridgePath)));

      string runtimeSha = BuildSha(typeof(IForeignPatchHost).Assembly);
      string bridgeSha = BuildSha(bridgeAssembly);

      if (runtimeSha != null && bridgeSha != null && runtimeSha != bridgeSha) {
        log($"Bridge at {bridgePath} was built from {bridgeSha}, the Concord runtime from {runtimeSha}. They ship as a pair. Reinstall both from one release; coexistence stays off.");
        return null;
      }

      Type bridgeType = null;
      foreach (Type type in bridgeAssembly.GetTypes()) {
        if (typeof(IForeignPatchHost).IsAssignableFrom(type) && !type.IsInterface) {
          bridgeType = type;
          break;
        }
      }

      if (bridgeType == null) {
        log("No IForeignPatchHost implementation found in bridge assembly.");
        return null;
      }

      IForeignPatchHost bridge = (IForeignPatchHost)Activator.CreateInstance(bridgeType, [log]);

      log($"{CoexistenceLogMarkers.BridgeActive} {harmonyVersion}");

      return bridge;
    } catch (Exception ex) {
      log($"Exception loading bridge: {ex}");
      return null;
    }
  }


  internal static string BuildSha(Assembly assembly) {
    AssemblyInformationalVersionAttribute stamp = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

    if (stamp?.InformationalVersion == null) {
      return null;
    }

    int plus = stamp.InformationalVersion.IndexOf('+');
    return plus < 0 ? null : stamp.InformationalVersion.Substring(plus + 1);
  }

  internal static bool VersionSupported(Version found, Action<string> log) {
    string range = $"[{supportedFrom}, {supportedBefore})";

    if (found == null) {
      log($"Harmony version could not be read from its assembly name. Bridge requires {range}.");
      return false;
    }

    if (found >= supportedFrom && found < supportedBefore) {
      return true;
    }

    log($"Harmony version {found} not supported; bridge requires {range}.");
    return false;
  }

  private static IReadOnlyList<string> ActiveModRoots() {
    List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
    List<string> roots = new List<string>();

    if (mods == null) {
      return roots;
    }

    for (int i = 0; i < mods.Count; i++) {
      string root = mods[i]?.RootDir;
      if (!string.IsNullOrEmpty(root)) {
        roots.Add(root);
      }
    }

    return roots;
  }

  private static string FindHarmonyShipper(IReadOnlyList<string> roots) {
    for (int i = 0; i < roots.Count; i++) {
      if (Directory.Exists(roots[i]) && Directory.GetFiles(roots[i], "0Harmony.dll", SearchOption.AllDirectories).Length > 0) {
        return roots[i];
      }
    }

    return null;
  }

  private static string Location(Assembly assembly) {
    try {
      string location = assembly.Location;
      return string.IsNullOrEmpty(location) ? null : Path.GetFullPath(location);
    } catch (NotSupportedException) {
      return null;
    }
  }

  private static bool IsUnder(string path, string root) {
    string full;

    try {
      full = Path.GetFullPath(root);
    } catch (Exception) {
      return false;
    }

    if (full[full.Length - 1] != Path.DirectorySeparatorChar) {
      full += Path.DirectorySeparatorChar;
    }

    return path.StartsWith(full, StringComparison.OrdinalIgnoreCase);
  }
}
