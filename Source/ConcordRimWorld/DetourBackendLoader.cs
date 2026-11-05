using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Verse;

namespace Concord.RimWorld;

internal static class DetourBackendLoader {
  internal const string BackendFileName = "Concord.Host.Adapter.dll";

  internal const string RuntimeBuildKey = "ConcordBuild";

  internal static bool TryInstall(
      string modRootDir,
      Action<string> log,
      Action<string> error,
      Func<string[]> assemblyDirectories) {
    string backendPath = null;

    try {
      backendPath = Path.Combine(
          modRootDir,
          ModLayout.VersionDir,
          "Backend",
          BackendFileName);

      if (!File.Exists(backendPath)) {
        error(
            "[Concord.RimWorld] Concord's patching backend is missing: no " + BackendFileName + " at " +
            backendPath + ". It is the only backend there is, so Concord will not patch anything this " +
            "session. Reinstall the mod.");
        return false;
      }

      Assembly backend = Assembly.LoadFrom(backendPath);

      if (!PairedWithRuntime(backend, backendPath, log, error)) {
        return false;
      }

      MethodInfo install = FindInstall(backend);

      if (install == null) {
        error(
            "[Concord.RimWorld] Concord's patching backend at " + backendPath + " has no matching entry " +
            "point, so Concord will not patch anything this session. Reinstall the mod.");
        return false;
      }

      object selection = install.Invoke(null, [log, assemblyDirectories()]);

      if (!Installed(selection)) {
        error(
            "[Concord.RimWorld] Concord's patching backend at " + backendPath + " installed nothing on this " +
            "runtime, so Concord will not patch anything this session: " + Read(selection, "Message"));
        return false;
      }

      return true;
    } catch (Exception e) {
      error(
          "[Concord.RimWorld] Concord's patching backend at " + (backendPath ?? modRootDir) +
          " could not be installed, so Concord will not patch anything this session: " + e);
      return false;
    }
  }

  internal static MethodInfo FindInstall(Assembly backend) {
    return backend
        .GetType("Concord.Host.Adapter.AdapterEntryPoint", true)
        .GetMethod("Install", [typeof(Action<string>), typeof(string[])]);
  }

  private static bool Installed(object selection) {
    return selection != null && (bool)Read(selection, "Waylith");
  }

  private static object Read(object selection, string property) {
    return selection?.GetType().GetProperty(property)?.GetValue(selection);
  }

  private static bool PairedWithRuntime(
      Assembly backend,
      string backendPath,
      Action<string> log,
      Action<string> error) {
    string runtimeBuild = ModLayout.BuildSha(typeof(Patcher).Assembly);
    string backendBuild = DeclaredRuntimeBuild(backend);

    if (runtimeBuild == null || backendBuild == null) {
      log(
          "[Concord.RimWorld] Concord's patching backend at " + backendPath + " carries no " +
          RuntimeBuildKey + " stamp naming the Concord runtime build it was compiled against, so it " +
          "cannot be checked against the runtime loaded here. Installing it unchecked.");
      return true;
    }

    if (runtimeBuild == backendBuild) {
      return true;
    }

    error(
        "[Concord.RimWorld] Concord's patching backend at " + backendPath + " was built against Concord " +
        backendBuild + ", this game runs " + runtimeBuild + ". They ship as a pair, so Concord will not " +
        "patch anything this session. Reinstall both from one release.");
    return false;
  }

  private static string DeclaredRuntimeBuild(Assembly backend) {
    foreach (AssemblyMetadataAttribute stamp in backend.GetCustomAttributes<AssemblyMetadataAttribute>()) {
      if (stamp.Key == RuntimeBuildKey) {
        return stamp.Value;
      }
    }

    return null;
  }

  internal static string[] ModAssemblyDirectories() {
    List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
    List<Assembly> loaded = [];

    if (mods != null) {
      for (int i = 0; i < mods.Count; i++) {
        List<Assembly> assemblies = mods[i]?.assemblies?.loadedAssemblies;

        if (assemblies == null) {
          continue;
        }

        for (int j = 0; j < assemblies.Count; j++) {
          if (assemblies[j] != null) {
            loaded.Add(assemblies[j]);
          }
        }
      }
    }

    return AssemblyDirectories(loaded);
  }

  internal static string[] AssemblyDirectories(IReadOnlyList<Assembly> loaded) {
    HashSet<string> dirs = new HashSet<string>(StringComparer.Ordinal);

    for (int i = 0; i < loaded.Count; i++) {
      string location = AssemblyLocation(loaded[i]);

      if (!string.IsNullOrEmpty(location)) {
        dirs.Add(Path.GetDirectoryName(location));
      }
    }

    return [.. dirs];
  }

  private static string AssemblyLocation(Assembly assembly) {
    if (assembly == null) {
      return null;
    }

    try {
      string location = assembly.Location;
      return string.IsNullOrEmpty(location) ? null : Path.GetFullPath(location);
    } catch (NotSupportedException) {
      return null;
    }
  }
}
