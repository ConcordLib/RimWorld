using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Concord.Detour;
using Concord.Emit;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

public class DeferredDispatchTests
{
    private static RoutingDetourBackend NewUnflushedRouter(IDetourBackend inner, List<string> log)
    {
        return new RoutingDetourBackend(inner, log.Add);
    }

    private static MethodBase TargetMethod()
    {
        return typeof(DeferredDispatchTests).GetMethod(
            nameof(StaticTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );
    }

    private static MethodBase OtherTargetMethod()
    {
        return typeof(DeferredDispatchTests).GetMethod(
            nameof(OtherTarget),
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
    public void PreFlush_ApplyComposed_EnqueuesAndReturnsPendingHandle()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        IDetourHandle handle = router.ApplyComposed(target, added);

        Assert.Equal(0, inner.ApplyComposedCallCount);
        Assert.False(handle.IsApplied);
        Assert.Equal(RouteState.Unpinned, router.GetRoute(target));
    }

    [Fact]
    public void Flush_AppliesQueuedItemsInEnqueueOrder()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase first = TargetMethod();
        MethodBase second = OtherTargetMethod();
        IReadOnlyList<Injection> firstAdded = new List<Injection> { MakeInjection(first) };
        IReadOnlyList<Injection> secondAdded = new List<Injection> { MakeInjection(second) };

        IDetourHandle firstHandle = router.ApplyComposed(first, firstAdded);
        IDetourHandle secondHandle = router.ApplyComposed(second, secondAdded);

        router.Flush();

        Assert.Equal(2, inner.ApplyComposedCallCount);
        Assert.Same(first, inner.ApplyComposedCalls[0].Item1);
        Assert.Same(second, inner.ApplyComposedCalls[1].Item1);
        Assert.True(firstHandle.IsApplied);
        Assert.True(secondHandle.IsApplied);
        Assert.Same(first, firstHandle.Original);
        Assert.Same(second, secondHandle.Original);
    }

    [Fact]
    public void PostFlush_ApplyComposed_AppliesImmediately()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        router.Flush();
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        IDetourHandle handle = router.ApplyComposed(target, added);

        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.True(handle.IsApplied);
    }

    [Fact]
    public void DisposingPendingHandlePreFlush_FlushNeverAppliesIt()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase cancelled = TargetMethod();
        MethodBase kept = OtherTargetMethod();
        IReadOnlyList<Injection> cancelledAdded = new List<Injection> { MakeInjection(cancelled) };
        IReadOnlyList<Injection> keptAdded = new List<Injection> { MakeInjection(kept) };

        IDetourHandle cancelledHandle = router.ApplyComposed(cancelled, cancelledAdded);
        router.ApplyComposed(kept, keptAdded);

        cancelledHandle.Dispose();

        router.Flush();

        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.Same(kept, inner.ApplyComposedCalls[0].Item1);
    }

    [Fact]
    public void EagerScope_PreFlush_AppliesImmediately()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        IDetourHandle handle;
        using (router.EagerScope())
        {
            handle = router.ApplyComposed(target, added);
        }

        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.True(handle.IsApplied);

        router.Flush();

        Assert.Equal(1, inner.ApplyComposedCallCount);
    }

    [Fact]
    public void DoubleFlush_InnerCallCountUnchanged()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };
        router.ApplyComposed(target, added);

        router.Flush();
        int afterFirstFlush = inner.ApplyComposedCallCount;

        router.Flush();

        Assert.Equal(afterFirstFlush, inner.ApplyComposedCallCount);
    }

    [Fact]
    public void Flush_FirstItemThrows_SecondStillApplied_LogsFailureAndCompletes()
    {
        ThrowingFakeInner inner = new ThrowingFakeInner();
        MethodBase failing = TargetMethod();
        inner.ThrowFor(failing, new InvalidOperationException("boom"));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase succeeding = OtherTargetMethod();
        IReadOnlyList<Injection> failingAdded = new List<Injection> { MakeInjection(failing) };
        IReadOnlyList<Injection> succeedingAdded = new List<Injection> { MakeInjection(succeeding) };

        router.ApplyComposed(failing, failingAdded);
        router.ApplyComposed(succeeding, succeedingAdded);

        router.Flush();

        Assert.Equal(1, inner.ApplyComposedCallCount);
        Assert.Same(succeeding, inner.ApplyComposedCalls[0].Item1);
        Assert.Contains(log, line => line.Contains("deferred apply failed"));
        Assert.Contains(log, line => line.Contains(CoexistenceLogMarkers.FlushComplete));
    }

    [Fact]
    public void DisposingPendingHandlePostFlush_DisposesRealInnerHandle()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase target = TargetMethod();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };
        IDetourHandle handle = router.ApplyComposed(target, added);

        router.Flush();

        FakeHandle realHandle = inner.ApplyComposedReturnedHandles[0];

        handle.Dispose();

        Assert.False(handle.IsApplied);
        Assert.Equal(1, realHandle.DisposeCallCount);
    }

    [Fact]
    public void Apply_PreFlush_AppliesImmediately_AndPinsRaw()
    {
        FakeInner inner = new FakeInner();
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        MethodBase target = TargetMethod();
        MethodInfo replacement = typeof(DeferredDispatchTests).GetMethod(
            nameof(OtherTarget),
            BindingFlags.NonPublic | BindingFlags.Static
        );

        router.Apply(target, replacement);

        Assert.Equal(1, inner.ApplyCallCount);
        Assert.Equal(RouteState.Raw, router.GetRoute(target));
    }

    [Fact]
    public void ApplyComposedRouted_NullBridgePath_ThrowingInner_LeavesTargetUnpinned_AllowsRerouting()
    {
        ThrowingFakeInner inner = new ThrowingFakeInner();
        MethodBase target = TargetMethod();
        inner.ThrowFor(target, new InvalidOperationException("apply failed"));
        List<string> log = new List<string>();
        RoutingDetourBackend router = NewUnflushedRouter(inner, log);
        router.Flush();
        IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

        Assert.Throws<InvalidOperationException>(() => router.ApplyComposed(target, added));
        Assert.Equal(RouteState.Unpinned, router.GetRoute(target));

        inner.ClearThrow(target);
        FakeBridge bridge = new FakeBridge();
        FakeHandle bridgeHandle = new FakeHandle { Original = target };
        bridge.Enqueue(BridgeRouteResult.Routed(bridgeHandle));
        router.ActivateBridge(bridge);

        IDetourHandle result = router.ApplyComposed(target, added);

        Assert.Equal(1, bridge.TryRouteCallCount);
        Assert.Same(bridgeHandle, result);
        Assert.Equal(RouteState.Bridge, router.GetRoute(target));
    }

    private class ThrowingFakeInner : IDetourBackend
    {
        private readonly Dictionary<MethodBase, Exception> failures = new Dictionary<MethodBase, Exception>();

        public int ApplyCallCount;
        public int ApplyComposedCallCount;
        public List<Tuple<MethodBase, IReadOnlyList<Injection>>> ApplyComposedCalls =
            new List<Tuple<MethodBase, IReadOnlyList<Injection>>>();

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
            if (failures.TryGetValue(target, out Exception exception))
            {
                throw exception;
            }

            ApplyComposedCallCount++;
            ApplyComposedCalls.Add(Tuple.Create(target, added));
            return new FakeHandle { Original = target };
        }
    }
}
