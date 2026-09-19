using Concord.Detour;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using Concord.RimWorld;
using HarmonyLib;

namespace Concord.RimWorld.Tests;

[Collection(GameAssemblies.Name)]
public class HarmonyProbeTests
{
    static HarmonyProbeTests()
    {
        typeof(HarmonyLib.Harmony).GetType();
    }

    // A fixed list, not the live domain. Reading names off AppDomain.GetAssemblies() while another
    // collection is still loading game dlls trips a mono loader assertion and kills the runner.
    private static Assembly[] Loaded()
    {
        return new[] { typeof(HarmonyLib.Harmony).Assembly, typeof(HarmonyProbe).Assembly, typeof(HarmonyProbeTests).Assembly };
    }

    private static Func<IReadOnlyList<string>> RootsOf(params string[] roots)
    {
        return () => roots;
    }

    private static Func<IReadOnlyList<string>> HarmonyRoot()
    {
        return RootsOf(Path.GetDirectoryName(typeof(HarmonyLib.Harmony).Assembly.Location));
    }

    [Fact]
    public void FindActiveHarmony_ReturnsHarmonyWhenItSitsUnderAnActiveModRoot()
    {
        Assembly[] loaded = Loaded();
        Assembly result = HarmonyProbe.FindActiveHarmony(() => loaded, HarmonyRoot(), _ => { });
        Assert.NotNull(result);
    }

    [Fact]
    public void FindActiveHarmony_ReturnsNullWhenHarmonyNotLoaded()
    {
        Assembly[] loaded = Array.FindAll(
            Loaded(),
            a => a.GetName().Name != "0Harmony"
        );
        Assembly result = HarmonyProbe.FindActiveHarmony(() => loaded, HarmonyRoot(), _ => { });
        Assert.Null(result);
    }

    [Fact]
    public void FindActiveHarmony_ReturnsNullAndLogsWhenHarmonyBelongsToNoActiveMod()
    {
        Assembly[] loaded = Loaded();
        string logOutput = null;

        Assembly result = HarmonyProbe.FindActiveHarmony(
            () => loaded,
            RootsOf(Path.Combine(Path.GetTempPath(), "not-a-real-mod")),
            log => logOutput = log
        );

        Assert.Null(result);
        Assert.Contains("installed but not enabled", logOutput);
    }

    private static Assembly MemoryLoadedHarmony()
    {
        return System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("0Harmony"),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
    }

    [Fact]
    public void FindActiveHarmony_AcceptsMemoryLoadedHarmonyWhenAnActiveModShipsIt()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "Assemblies"));
        File.WriteAllBytes(Path.Combine(root, "Assemblies", "0Harmony.dll"), new byte[] { 0 });
        string logOutput = null;

        try
        {
            Assembly result = HarmonyProbe.FindActiveHarmony(
                () => new[] { MemoryLoadedHarmony() },
                RootsOf(root),
                log => logOutput = log);

            Assert.NotNull(result);
            Assert.Contains("coexistence stays on", logOutput);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindActiveHarmony_RejectsMemoryLoadedHarmonyWhenNoActiveModShipsIt()
    {
        string logOutput = null;

        Assembly result = HarmonyProbe.FindActiveHarmony(
            () => new[] { MemoryLoadedHarmony() },
            RootsOf(Path.Combine(Path.GetTempPath(), "not-a-real-mod")),
            log => logOutput = log);

        Assert.Null(result);
        Assert.Contains("loaded from memory", logOutput);
    }

    [Fact]
    public void TryLoadBridge_ReturnsNullAndLogsWhenHarmonyAbsent()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string bridgeDir = Path.Combine(tempRoot, "Current", "Bridge");
        Directory.CreateDirectory(bridgeDir);
        File.WriteAllText(Path.Combine(bridgeDir, "ConcordRimWorld.Harmony.dll"), "dummy");

        string logOutput = null;
        IForeignPatchHost result = HarmonyProbe.TryLoadBridge(
            tempRoot,
            log => logOutput = log,
            () => Array.FindAll(
                Loaded(),
                a => a.GetName().Name != "0Harmony"
            ),
            HarmonyRoot()
        );

        try
        {
            Assert.Null(result);
            Assert.NotNull(logOutput);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void TryLoadBridge_ReturnsNullAndLogsWhenBridgeFileNotFound()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempRoot);

        string logOutput = null;
        IForeignPatchHost result = HarmonyProbe.TryLoadBridge(
            tempRoot,
            log => logOutput = log,
            () => Loaded(),
            HarmonyRoot()
        );

        try
        {
            Assert.Null(result);
            Assert.NotNull(logOutput);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void TryLoadBridge_ReturnsNullWhenHarmonyBelongsToNoActiveMod()
    {
        string repoRoot = ResolveRepoRoot();
        string logOutput = null;

        IForeignPatchHost bridge = HarmonyProbe.TryLoadBridge(
            repoRoot,
            log => logOutput = log,
            () => Loaded(),
            RootsOf(Path.Combine(Path.GetTempPath(), "not-a-real-mod"))
        );

        Assert.Null(bridge);
        Assert.Equal("Harmony not present; bridge cannot be loaded.", logOutput);
    }

    [Fact]
    public void VersionSupported_ReturnsFalseForVersion2_3_0()
    {
        string logOutput = null;
        bool result = HarmonyProbe.VersionSupported(
            new Version(2, 3, 0),
            log => logOutput = log
        );

        Assert.False(result);
        Assert.NotNull(logOutput);
    }

    [Fact]
    public void VersionSupported_ReturnsFalseForVersion2_4_0()
    {
        string logOutput = null;
        bool result = HarmonyProbe.VersionSupported(
            new Version(2, 4, 0),
            log => logOutput = log
        );

        Assert.False(result);
        Assert.NotNull(logOutput);
    }

    [Fact]
    public void VersionSupported_ReturnsTrueForVersion2_4_1()
    {
        string logOutput = null;
        bool result = HarmonyProbe.VersionSupported(
            new Version(2, 4, 1),
            log => logOutput = log
        );

        Assert.True(result);
        Assert.Null(logOutput);
    }

    [Fact]
    public void VersionSupported_ReturnsTrueForVersion2_4_9()
    {
        string logOutput = null;
        bool result = HarmonyProbe.VersionSupported(
            new Version(2, 4, 9),
            log => logOutput = log
        );

        Assert.True(result);
        Assert.Null(logOutput);
    }

    [Fact]
    public void VersionSupported_ReturnsFalseForVersion2_5_0()
    {
        string logOutput = null;
        bool result = HarmonyProbe.VersionSupported(
            new Version(2, 5, 0),
            log => logOutput = log
        );

        Assert.False(result);
        Assert.NotNull(logOutput);
    }

    [Fact]
    public void VersionSupported_ReturnsFalseForVersion3_0_0()
    {
        string logOutput = null;
        bool result = HarmonyProbe.VersionSupported(
            new Version(3, 0, 0),
            log => logOutput = log
        );

        Assert.False(result);
        Assert.NotNull(logOutput);
    }


    [Fact]
    public void TryLoadBridge_HarmonyPresent_ReturnsBridgeAndLogsBridgeActive()
    {
        string repoRoot = ResolveRepoRoot();
        string logOutput = null;

        IForeignPatchHost bridge = HarmonyProbe.TryLoadBridge(
            repoRoot,
            log => logOutput = log,
            () => Loaded(),
            HarmonyRoot()
        );

        Assert.NotNull(bridge);
        Assert.NotNull(logOutput);
        Assert.Contains(CoexistenceLogMarkers.BridgeActive, logOutput);
    }

    private static string ResolveRepoRoot()
    {
        Uri codeBase = new Uri(typeof(HarmonyProbeTests).Assembly.CodeBase);
        string current = Path.GetDirectoryName(codeBase.LocalPath);
        while (current != null)
        {
            // Derived from the probe's own constant rather than repeated here, so renaming the
            // staged bridge cannot silently leave this walking for a file that no longer exists.
            string candidate = Path.Combine(current, HarmonyProbe.BridgeRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("Could not resolve repo root from test assembly location.");
    }
}
