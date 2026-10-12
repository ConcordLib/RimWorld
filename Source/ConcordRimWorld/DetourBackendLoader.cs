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
      Action<string> warn,
      Func<string[]> assemblyDirectories) {
    string backendPath = null;

    try {
      backendPath = Path.Combine(
          modRootDir,
          HarmonyProbe.VersionDir,
          "Backend",
          BackendFileName);

      if (!File.Exists(backendPath)) {
        return false;
      }

      Assembly backend = Assembly.LoadFrom(backendPath);

      if (!PairedWithRuntime(backend, backendPath, warn)) {
        return false;
      }

      Type entryPoint = backend.GetType("Concord.Host.Adapter.AdapterEntryPoint", true);
      MethodInfo install = entryPoint.GetMethod(
          "Install",
          [typeof(Action<string>), typeof(bool?), typeof(string[])]);

      if (install == null) {
        warn(
            "[Concord.RimWorld] An optional detour backend ships at " + backendPath +
            " but has no matching entry point, so Concord stays on its default backend.");
        return false;
      }

      install.Invoke(null, [log, null, assemblyDirectories()]);
      log("[Concord.RimWorld] An optional detour backend ships alongside this mod and is installed.");
      return true;
    } catch (Exception e) {
      warn(
          "[Concord.RimWorld] An optional detour backend ships at " + (backendPath ?? modRootDir) +
          " but could not be installed, so Concord stays on its default backend: " + e);
      return false;
    }
  }

  private static bool PairedWithRuntime(Assembly backend, string backendPath, Action<string> warn) {
    string runtimeBuild = HarmonyProbe.BuildSha(typeof(Patcher).Assembly);
    string backendBuild = DeclaredRuntimeBuild(backend);

    if (runtimeBuild == null || backendBuild == null) {
      warn(
          "[Concord.RimWorld] The optional detour backend at " + backendPath + " carries no " +
          RuntimeBuildKey + " stamp naming the Concord runtime build it was compiled against, so it " +
          "cannot be checked against the runtime loaded here. Installing it unchecked.");
      return true;
    }

    if (runtimeBuild == backendBuild) {
      return true;
    }

    warn(
        "[Concord.RimWorld] The optional detour backend at " + backendPath + " was built against Concord " +
        backendBuild + ", this game runs " + runtimeBuild + ". They ship as a pair. Reinstall both from one " +
        "release; Concord stays on its default backend.");
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
