using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;
using Concord.Detour;
using Concord.Emit;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

public class RoutingDetourBackendTests
{
    private static RoutingDetourBackend NewRouter(IDetourBackend inner, List<string> log)
    {
        RoutingDetourBackend router = new RoutingDetourBackend(inner, log.Add);
        router.Flush();
        return router;
    }

    private static MethodBase TargetMethod()
    {
        return typeof(RoutingDetourBackendTests).GetMethod(
            nameof(StaticTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StaticTarget()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OtherTarget()
    {
    }

    private static Injection MakeInjection(MethodBase method)
    {
        return new Injection(method, new InjectAt.Head(), "test-owner", 0);
    }

    [Fact]
    public void NullBridge_PinsRawAndCallsInnerOnce()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        IDetourHandle handle = router.ApplyComposed(target, added);

        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.Equal(RouteState.Raw, router.GetRoute(target));
        Assert.NotNull(handle);
    }

    [Fact]
    public void ActiveBridge_NotContested_PinsRawAndCallsInner()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.NotContested());
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.ActivateBridge(bridge);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        router.ApplyComposed(target, added);

        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.Equal(1, bridge.TryRouteCallCount);
        Assert.Equal(RouteState.Raw, router.GetRoute(target));
    }

    [Fact]
    public void ActiveBridge_Routed_PinsBridgeAndReturnsScriptedHandleWithoutCallingInner()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        FakeHandle scriptedHandle = new FakeHandle();
        bridge.Enqueue(BridgeRouteResult.Routed(scriptedHandle));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.ActivateBridge(bridge);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        IDetourHandle handle = router.ApplyComposed(target, added);

        Assert.Equal(0, inner.ApplyComposedCallCount);
        Assert.Same(scriptedHandle, handle);
        Assert.Equal(RouteState.Bridge, router.GetRoute(target));
        Assert.Contains(log, line => line.Contains(CoexistenceLogMarkers.RoutedContested));
    }

    [Fact]
    public void PinStability_RawPinSurvivesLateActivateBridge()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        router.ApplyComposed(target, added);

        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.Routed(new FakeHandle()));
        router.ActivateBridge(bridge);

        router.ApplyComposed(target, added);

        Assert.Equal(RouteState.Raw, router.GetRoute(target));
        Assert.Equal(0, bridge.TryRouteCallCount);
        Assert.Equal(2, inner.ApplyComposedCallCount);
    }

    [Fact]
    public void Rejected_ThrowsWithReason_AndSecondCallThrowsWithoutCallingTryRoute()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.Rejected("no-ctor-around"));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.ActivateBridge(bridge);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(
            () => router.ApplyComposed(target, added)
        );
        Assert.Contains("no-ctor-around", first.Message);
        Assert.Equal(1, bridge.TryRouteCallCount);

        InvalidOperationException second = Assert.Throws<InvalidOperationException>(
            () => router.ApplyComposed(target, added)
        );
        Assert.Contains("no-ctor-around", second.Message);
        Assert.Equal(1, bridge.TryRouteCallCount);
        Assert.Equal(RouteState.Rejected, router.GetRoute(target));
    }

    [Fact]
    public void BridgePinned_SecondApplyComposedCallsApplyToRoutedNotTryRouteAgain()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.Routed(new FakeHandle()));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.ActivateBridge(bridge);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        router.ApplyComposed(target, added);
        router.ApplyComposed(target, added);

        Assert.Equal(1, bridge.TryRouteCallCount);
        Assert.Equal(1, bridge.ApplyToRoutedCallCount);
    }

    [Fact]
    public void Apply_OnUnpinned_PinsRaw_AndSubsequentApplyComposedDelegatesToInnerEvenWithActiveBridge()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        MethodBase target = TargetMethod();
        MethodInfo replacement = typeof(RoutingDetourBackendTests).GetMethod(
            nameof(OtherTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );

        router.Apply(target, replacement);

        Assert.Equal(RouteState.Raw, router.GetRoute(target));

        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.Routed(new FakeHandle()));
        router.ActivateBridge(bridge);

        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };
        router.ApplyComposed(target, added);

        Assert.Equal(0, bridge.TryRouteCallCount);
        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.Equal(RouteState.Raw, router.GetRoute(target));
    }

    [Fact]
    public void Apply_OnBridgePinnedTarget_Throws()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.Routed(new FakeHandle()));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.ActivateBridge(bridge);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };
        router.ApplyComposed(target, added);

        MethodInfo replacement = typeof(RoutingDetourBackendTests).GetMethod(
            nameof(OtherTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.Throws<InvalidOperationException>(() => router.Apply(target, replacement));
    }

    [Fact]
    public void Apply_OnRejectedTarget_Throws()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.Rejected("no-ctor-around"));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.ActivateBridge(bridge);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };
        Assert.Throws<InvalidOperationException>(() => router.ApplyComposed(target, added));

        MethodInfo replacement = typeof(RoutingDetourBackendTests).GetMethod(
            nameof(OtherTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.Throws<InvalidOperationException>(() => router.Apply(target, replacement));
    }

    [Fact]
    public void RouteEverythingTrue_PassesForceRouteTrueToTryRoute()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.NotContested());
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.RouteEverything = true;
        router.ActivateBridge(bridge);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        router.ApplyComposed(target, added);

        Assert.True(bridge.LastForceRoute);
    }

    [Fact]
    public void RawPinnedTargets_ReturnsExactlyTheRawPinnedSet()
    {
        FakeInner inner = new FakeInner();
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(BridgeRouteResult.NotContested());
        bridge.Enqueue(BridgeRouteResult.Routed(new FakeHandle()));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);
        router.ActivateBridge(bridge);
        MethodBase rawTarget = TargetMethod();
        MethodBase bridgeTarget = typeof(RoutingDetourBackendTests).GetMethod(
            nameof(OtherTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );
        IReadOnlyList<Injection> rawAdded = new List<Injection> { MakeInjection(rawTarget) };
        IReadOnlyList<Injection> bridgeAdded = new List<Injection> { MakeInjection(bridgeTarget) };

        router.ApplyComposed(rawTarget, rawAdded);
        router.ApplyComposed(bridgeTarget, bridgeAdded);

        IReadOnlyCollection<MethodBase> rawTargets = router.RawPinnedTargets();

        Assert.Single(rawTargets);
        Assert.Contains(rawTarget, rawTargets);
        Assert.DoesNotContain(bridgeTarget, rawTargets);
    }

    [Fact]
    public void Normalization_DistinctReflectedMethodInfoInstancesShareOnePin()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewRouter(inner, log);

        MethodBase first = typeof(NormalizationTarget).GetMethod("M");
        MethodBase second = null;
        foreach (MethodInfo candidate in typeof(NormalizationTarget).GetMethods())
        {
            if (candidate.Name == "M")
            {
                second = candidate;
                break;
            }
        }

        Assert.NotNull(second);

        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(first) };

        router.ApplyComposed(first, added);
        router.ApplyComposed(second, added);

        Assert.Equal(2, inner.ApplyComposedCallCount);
        Assert.Same(inner.ApplyComposedCalls[0].Item1, inner.ApplyComposedCalls[1].Item1);
        Assert.Equal(RouteState.Raw, router.GetRoute(first));
        Assert.Equal(RouteState.Raw, router.GetRoute(second));
    }


    [Fact]
    public void DescribeTarget_NullDeclaringType_DoesNotThrow()
    {
        DynamicMethod dynamicMethod = new DynamicMethod("GlobalMethod", typeof(void), Type.EmptyTypes);
        ILGenerator il = dynamicMethod.GetILGenerator();
        il.Emit(OpCodes.Ret);

        Assert.Null(dynamicMethod.DeclaringType);

        MethodInfo describeTarget = typeof(RoutingDetourBackend).GetMethod(
            "DescribeTarget",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        string description = (string)describeTarget.Invoke(null, new object[] { dynamicMethod });

        Assert.Equal("<module>.GlobalMethod", description);
    }

    private class NormalizationTarget
    {
        public void M()
        {
        }
    }
}
