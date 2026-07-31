using System;
using System.Reflection;

namespace Concord.RimWorld;

/// <summary>Reads the shipped adapter and Concord runtime versions off their assemblies.</summary>
internal static class VersionInfo {
    private static string adapter;
    private static string runtime;

    internal static string Adapter => adapter ??= Read(typeof(VersionInfo).Assembly);

    internal static string Runtime => runtime ??= Read(typeof(Patcher).Assembly);

    internal static string Line => "Concord v" + Adapter + " (v" + Runtime + ")";

    private static string Read(Assembly assembly) {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version)) {
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        int metadata = version.IndexOf('+');
        return metadata < 0 ? version : version.Substring(0, metadata);
    }
}
