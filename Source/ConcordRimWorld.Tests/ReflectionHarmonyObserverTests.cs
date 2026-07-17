using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using HarmonyLib;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

[Collection("HarmonySerial")]
public class ReflectionHarmonyObserverTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TargetForPatching()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnpatchedTarget()
    {
    }

    [Fact]
    public void PatchedTarget_ReturnsOwner()
    {
        MethodBase target = typeof(ReflectionHarmonyObserverTests).GetMethod(
            nameof(TargetForPatching),
            BindingFlags.NonPublic | BindingFlags.Static
        );

        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("reflection.observer.test");
        HarmonyMethod postfix = new HarmonyMethod(typeof(ReflectionHarmonyObserverTests).GetMethod(
            nameof(DummyPostfix),
            BindingFlags.NonPublic | BindingFlags.Static
        ));
        harmony.Patch(target, postfix: postfix);

        try
        {
            Func<Assembly[]> loadedAssemblies = () => AppDomain.CurrentDomain.GetAssemblies();
            List<string> logs = new List<string>();
            Action<string> log = (msg) => logs.Add(msg);

            Func<MethodBase, IReadOnlyList<string>> lookup = ReflectionHarmonyObserver.TryCreateForeignOwnerLookup(loadedAssemblies, log);

            Assert.NotNull(lookup);
            IReadOnlyList<string> owners = lookup(target);
            Assert.NotNull(owners);
            Assert.Single(owners);
            Assert.Equal("reflection.observer.test", owners[0]);
        }
        finally
        {
            harmony.UnpatchAll("reflection.observer.test");
        }
    }

    [Fact]
    public void UnpatchedTarget_ReturnsEmpty()
    {
        MethodBase target = typeof(ReflectionHarmonyObserverTests).GetMethod(
            nameof(UnpatchedTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Func<Assembly[]> loadedAssemblies = () => AppDomain.CurrentDomain.GetAssemblies();
        List<string> logs = new List<string>();
        Action<string> log = (msg) => logs.Add(msg);

        Func<MethodBase, IReadOnlyList<string>> lookup = ReflectionHarmonyObserver.TryCreateForeignOwnerLookup(loadedAssemblies, log);

        Assert.NotNull(lookup);
        IReadOnlyList<string> owners = lookup(target);
        Assert.NotNull(owners);
        Assert.Empty(owners);
    }

    [Fact]
    public void NoHarmony_ReturnsNull()
    {
        Func<Assembly[]> loadedAssemblies = () => Array.Empty<Assembly>();
        List<string> logs = new List<string>();
        Action<string> log = (msg) => logs.Add(msg);

        Func<MethodBase, IReadOnlyList<string>> lookup = ReflectionHarmonyObserver.TryCreateForeignOwnerLookup(loadedAssemblies, log);

        Assert.Null(lookup);
        Assert.Single(logs);
    }

    private static void DummyPostfix()
    {
    }
}
