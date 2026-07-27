using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using Concord.Detour;
using Concord.Emit;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

[Collection("AdapterWiringSerial")]
public class AdapterWiringTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProbeTarget()
    {
    }

    private static MethodBase ProbeMethod()
    {
        return typeof(AdapterWiringTests).GetMethod(nameof(ProbeTarget), BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static Injection MakeInjection(MethodBase method)
    {
        return new Injection(method, new InjectAt.Head(), "test-owner", 0);
    }

    private static WireContext NewContext(
        FakeInner inner,
        List<string> log,
        List<Action> scheduled,
        Func<string, Action<string>, IForeignPatchHost> loadBridge,
        bool bridgeRoutingEnabled,
        bool routeEverything)
    {
        return new WireContext
        {
            Settings = new ConcordSettings
            {
                BridgeRoutingEnabled = bridgeRoutingEnabled,
                RouteEverythingWhenHarmonyPresent = routeEverything
            },
            ModRootDir = "unused",
            Schedule = scheduled.Add,
            LoadBridge = loadBridge,
            Log = log.Add,
            DialogOnce = log.Add,
            ApplyEagerTier = () => {
                MethodBase target = ProbeMethod();
                IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };
                DetourBackend.Current.ApplyComposed(target, added);
            }
        };
    }

    [Fact]
    public void Wire_InstallsRouterRunsOwnPatchesAndSchedulesCheckpoints()
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();

        try
        {
            WireContext context = NewContext(inner, log, scheduled, (root, l) => null, false, false);

            RimWorldAdapter.Wire(context);

            Assert.IsType<RoutingDetourBackend>(DetourBackend.Current);
            RoutingDetourBackend router = (RoutingDetourBackend)DetourBackend.Current;

            Assert.Equal(1, inner.ApplyComposedCallCount);

            MethodBase consumerTarget = typeof(AdapterWiringTests).GetMethod(
                nameof(ConsumerTarget), BindingFlags.NonPublic | BindingFlags.Static);
            IReadOnlyList<Injection> consumerInjection = new List<Injection> { MakeInjection(consumerTarget) };
            IDetourHandle consumerHandle = router.ApplyComposed(consumerTarget, consumerInjection);

            // No queue any more: a consumer patch reaches the inner backend as soon as it is applied.
            Assert.Equal(2, inner.ApplyComposedCallCount);
            Assert.NotNull(consumerHandle);

            Assert.Single(scheduled);

            Action checkpointOne = scheduled[0];
            scheduled.Clear();
            checkpointOne();

            Assert.Single(scheduled);
            Assert.Equal(2, inner.ApplyComposedCallCount);

            // Both checkpoints now only run the contention watcher; there is no queue left to drain,
            // so neither one applies anything.
            Action checkpointTwo = scheduled[0];
            checkpointTwo();

            Assert.Equal(2, inner.ApplyComposedCallCount);
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConsumerTarget()
    {
    }

    [Fact]
    public void Wire_ContendedRawPin_WarnsOnCheckpoint()
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();

        try
        {
            WireContext context = NewContext(inner, log, scheduled, (root, l) => null, false, false);

            RimWorldAdapter.Wire(context);

            FakeBridge lateBridge = new FakeBridge
            {
                ForeignOwnersFunc = _ => new List<string> { "foreign.mod" }.AsReadOnly(),

                // The watcher only sweeps raw pins when the notifier could not install. With the hook
                // active this method would have been promoted instead of reported.
                NotifierInstalls = false
            };
            lateBridge.Enqueue(ForeignRouteResult.NotContested());

            context.LoadBridge = (root, l) => lateBridge;
            RimWorldAdapter.TryLateActivation(context);

            Action checkpointOne = scheduled[0];
            checkpointOne();

            Assert.Contains(log, line => line.Contains(CoexistenceLogMarkers.LateContention));
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }

    [Fact]
    public void Wire_BridgeRoutingDisabled_LoadBridgeNeverCalledButRouterInstalled()
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();
        int loadBridgeCallCount = 0;

        try
        {
            WireContext context = NewContext(
                inner, log, scheduled,
                (root, l) => { loadBridgeCallCount++; return null; },
                false, false);

            RimWorldAdapter.Wire(context);

            Assert.Equal(0, loadBridgeCallCount);
            Assert.IsType<RoutingDetourBackend>(DetourBackend.Current);
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }

    [Fact]
    public void Wire_BridgeRoutingEnabled_LoadBridgeCalledOnceAndActivated()
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();
        int loadBridgeCallCount = 0;
        FakeBridge bridge = new FakeBridge();
        bridge.Enqueue(ForeignRouteResult.Routed(new FakeHandle()));

        try
        {
            WireContext context = NewContext(
                inner, log, scheduled,
                (root, l) => { loadBridgeCallCount++; return bridge; },
                true, false);

            RimWorldAdapter.Wire(context);

            Assert.Equal(1, loadBridgeCallCount);
            Assert.False(RimWorldAdapter.HasLateActivationHandler);

            RoutingDetourBackend router = (RoutingDetourBackend)DetourBackend.Current;

            MethodBase target = typeof(AdapterWiringTests).GetMethod(
                nameof(RoutedTarget), BindingFlags.NonPublic | BindingFlags.Static);
            IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

            IDetourHandle handle = router.ApplyComposed(target, added);

            Assert.Equal(1, bridge.TryRouteCallCount);
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RoutedTarget()
    {
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wire_RouteEverythingMirrorsSetting(bool routeEverything)
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();

        try
        {
            WireContext context = NewContext(inner, log, scheduled, (root, l) => null, false, routeEverything);

            RimWorldAdapter.Wire(context);

            RoutingDetourBackend router = (RoutingDetourBackend)DetourBackend.Current;
            Assert.Equal(routeEverything, router.RouteEverything);
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }

    [Fact]
    public void TryLateActivation_BridgeArrivesAfterWire_RoutesAndUpdatesWatcherLookup()
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();

        try
        {
            WireContext context = NewContext(inner, log, scheduled, (root, l) => null, true, false);

            RimWorldAdapter.Wire(context);

            Assert.True(RimWorldAdapter.HasLateActivationHandler);

            RoutingDetourBackend router = (RoutingDetourBackend)DetourBackend.Current;
            Assert.Equal(RouteState.Raw, router.GetRoute(ProbeMethod()));

            FakeBridge lateBridge = new FakeBridge
            {
                ForeignOwnersFunc = _ => new List<string> { "late.owner" }.AsReadOnly(),

                // As above: this asserts the watcher's lookup switches to the bridge, which only
                // sweeps raw pins on the no-hook path.
                NotifierInstalls = false
            };
            lateBridge.Enqueue(ForeignRouteResult.Routed(new FakeHandle()));

            context.LoadBridge = (root, l) => lateBridge;

            RimWorldAdapter.TryLateActivation(context);
            Assert.False(RimWorldAdapter.HasLateActivationHandler);

            MethodBase lateTarget = typeof(AdapterWiringTests).GetMethod(
                nameof(LateTarget), BindingFlags.NonPublic | BindingFlags.Static);
            IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(lateTarget) };

            router.ApplyComposed(lateTarget, added);

            Assert.Equal(1, lateBridge.TryRouteCallCount);
            Assert.Equal(RouteState.Bridge, router.GetRoute(lateTarget));

            Action checkpointOne = scheduled[0];
            checkpointOne();

            Assert.Contains(log, line => line.Contains("late.owner"));
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LateTarget()
    {
    }


    [Fact]
    public void TryLateActivation_SecondConcurrentCall_IsANoOp()
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();

        try
        {
            WireContext context = NewContext(inner, log, scheduled, (root, l) => null, true, false);

            RimWorldAdapter.Wire(context);

            int loadBridgeCallCount = 0;
            FakeBridge lateBridge = new FakeBridge();
            lateBridge.Enqueue(ForeignRouteResult.Routed(new FakeHandle()));

            context.LoadBridge = (root, l) => {
                loadBridgeCallCount++;
                return lateBridge;
            };

            RimWorldAdapter.TryLateActivation(context);
            RimWorldAdapter.TryLateActivation(context);

            Assert.Equal(1, loadBridgeCallCount);
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }

    [Fact]
    public void TryLateActivation_BridgeNeverArrives_StillDetachesHandler()
    {
        FakeInner inner = new FakeInner();
        DetourBackend.Current = inner;
        List<string> log = new List<string>();
        List<Action> scheduled = new List<Action>();

        try
        {
            WireContext context = NewContext(inner, log, scheduled, (root, l) => null, true, false);

            RimWorldAdapter.Wire(context);
            Assert.True(RimWorldAdapter.HasLateActivationHandler);

            RimWorldAdapter.TryLateActivation(context);

            Assert.False(RimWorldAdapter.HasLateActivationHandler);
        }
        finally
        {
            DetourBackend.Current = inner;
            RimWorldAdapter.ResetForTests();
        }
    }
}
