using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Concord.Detour;
using Concord.Emit;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

// Patches used to sit in a queue until a Flush after mod loading, so Concord could see Harmony's
// startup patches before deciding how to route. The notifier hook replaced that: applies take effect
// immediately and anything Harmony does later promotes the target onto the bridge.
public class DispatchTests
{
    [Fact]
    public void ApplyComposed_AppliesImmediately_AndPinsRaw()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = new RoutingDetourBackend(inner, log.Add);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        router.ApplyComposed(target, added);

        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.Equal(RouteState.Raw, router.GetRoute(target));
    }

    [Fact]
    public void Apply_AppliesImmediately_AndPinsRaw()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = new RoutingDetourBackend(inner, log.Add);
        MethodBase target = TargetMethod();
        MethodInfo replacement = typeof(DispatchTests).GetMethod(
            nameof(OtherTarget),
            BindingFlags.NonPublic | BindingFlags.Static);

        router.Apply(target, replacement);

        Assert.Equal(1, inner.ApplyCallCount);
        Assert.Equal(RouteState.Raw, router.GetRoute(target));
    }

    [Fact]
    public void DisposingHandle_DisposesTheInnerHandle()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = new RoutingDetourBackend(inner, log.Add);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        IDetourHandle handle = router.ApplyComposed(target, added);
        FakeHandle realHandle = inner.ApplyComposedReturnedHandles[0];

        handle.Dispose();

        Assert.False(handle.IsApplied);
        Assert.Equal(1, realHandle.DisposeCallCount);
    }

    [Fact]
    public void ApplyComposedRouted_NullBridgePath_ThrowingInner_LeavesTargetUnpinned_AllowsRerouting()
    {
        ThrowingFakeInner inner = new ThrowingFakeInner();
        MethodBase target = TargetMethod();
        inner.ThrowFor(target, new InvalidOperationException("apply failed"));
        List<string> log = new List<string>();
        RoutingDetourBackend router = new RoutingDetourBackend(inner, log.Add);
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        Assert.Throws<InvalidOperationException>(() => router.ApplyComposed(target, added));
        Assert.Equal(RouteState.Unpinned, router.GetRoute(target));

        inner.ClearThrow(target);
        FakeBridge bridge = new FakeBridge();
        FakeHandle bridgeHandle = new FakeHandle { Original = target };
        bridge.Enqueue(ForeignRouteResult.Routed(bridgeHandle));
        router.ActivateHost(bridge);

        IDetourHandle result = router.ApplyComposed(target, added);

        Assert.Equal(1, bridge.TryRouteCallCount);

        // The caller gets the router's stable wrapper so a later promotion can swap the detour
        // underneath it, so check delegation rather than reference identity.
        Assert.True(result.IsApplied);
        result.Dispose();
        Assert.Equal(1, bridgeHandle.DisposeCallCount);
        Assert.Equal(RouteState.Bridge, router.GetRoute(target));
    }

    private static MethodBase TargetMethod()
    {
        return typeof(DispatchTests).GetMethod(
            nameof(StaticTarget),
            BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static Injection MakeInjection(MethodBase method)
    {
        return new Injection(method, new InjectAt.Head(), "test-owner", 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StaticTarget()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OtherTarget()
    {
    }

    private class ThrowingFakeInner : IDetourBackend
    {
        private readonly Dictionary<MethodBase, Exception> failures = new Dictionary<MethodBase, Exception>();

        public int ApplyCallCount;
        public int ApplyComposedCallCount;

        public void ThrowFor(MethodBase target, Exception exception)
        {
            failures[target] = exception;
        }

        public void ClearThrow(MethodBase target)
        {
            failures.Remove(target);
        }

        public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
        {
            ApplyCallCount++;
            return new FakeHandle { Original = original };
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
        {
            ApplyComposedCallCount++;

            if (failures.TryGetValue(target, out Exception failure))
            {
                throw failure;
            }

            return new FakeHandle { Original = target };
        }
    }
}
