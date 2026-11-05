using System.Reflection;

namespace Concord.RimWorld;

internal static class ModLayout {
  internal const string VersionDir = "1.7";

  internal static string SimpleName(Assembly assembly) {
    string full = assembly.FullName;
    int comma = full.IndexOf(',');
    return comma < 0 ? full : full.Substring(0, comma);
  }

  internal static string BuildSha(Assembly assembly) {
    AssemblyInformationalVersionAttribute stamp = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

    if (stamp?.InformationalVersion == null) {
      return null;
    }

    int plus = stamp.InformationalVersion.IndexOf('+');
    return plus < 0 ? null : stamp.InformationalVersion.Substring(plus + 1);
  }
}
