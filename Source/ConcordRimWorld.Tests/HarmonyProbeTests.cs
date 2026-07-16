using System;
using System.IO;
using System.Reflection;
using Xunit;
using Concord.RimWorld;
using HarmonyLib;

namespace Concord.RimWorld.Tests;

public class HarmonyProbeTests
{
    static HarmonyProbeTests()
    {
        typeof(Harmony).GetType();
    }

    [Fact]
    public void HarmonyPresent_ReturnsTrueWhenHarmonyLoaded()
    {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        bool result = HarmonyProbe.HarmonyPresent(() => loaded);
        Assert.True(result);
    }

    [Fact]
    public void HarmonyPresent_ReturnsFalseWhenHarmonyNotLoaded()
    {
        Assembly[] loaded = Array.FindAll(
            AppDomain.CurrentDomain.GetAssemblies(),
            a => a.GetName().Name != "0Harmony"
        );
        bool result = HarmonyProbe.HarmonyPresent(() => loaded);
        Assert.False(result);
    }

    [Fact]
    public void TryLoadBridge_ReturnsNullAndLogsWhenHarmonyAbsent()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string bridgeDir = Path.Combine(tempRoot, "Current", "Bridge");
        Directory.CreateDirectory(bridgeDir);
        File.WriteAllText(Path.Combine(bridgeDir, "ConcordRimWorld.Harmony.dll"), "dummy");

        string logOutput = null;
        IHarmonyBridge result = HarmonyProbe.TryLoadBridge(
            tempRoot,
            log => logOutput = log,
            () => Array.FindAll(
                AppDomain.CurrentDomain.GetAssemblies(),
                a => a.GetName().Name != "0Harmony"
            )
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
        IHarmonyBridge result = HarmonyProbe.TryLoadBridge(
            tempRoot,
            log => logOutput = log,
            () => AppDomain.CurrentDomain.GetAssemblies()
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
}
