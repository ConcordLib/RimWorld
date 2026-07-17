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
        Func<string, Action<string>, IHarmonyBridge> loadBridge,
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
    public void Wire_InstallsRouterRunsEagerTierAndSchedulesCheckpointsAndFlush()
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

            Assert.Equal(1, inner.ApplyComposedCallCount);
            Assert.NotNull(consumerHandle);

            Assert.Single(scheduled);

            Action checkpointOne = scheduled[0];
            scheduled.Clear();
            checkpointOne();

            Assert.Single(scheduled);
            Assert.Equal(1, inner.ApplyComposedCallCount);

            Action flushAndCheckpointTwo = scheduled[0];
            flushAndCheckpointTwo();

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
                ForeignOwnersFunc = _ => new List<string> { "foreign.mod" }.AsReadOnly()
            };
            lateBridge.Enqueue(BridgeRouteResult.NotContested());

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
        bridge.Enqueue(BridgeRouteResult.Routed(new FakeHandle()));

        try
        {
            WireContext context = NewContext(
                inner, log, scheduled,
                (root, l) => { loadBridgeCallCount++; return bridge; },
                true, false);

            RimWorldAdapter.Wire(context);

            Assert.Equal(1, loadBridgeCallCount);

            RoutingDetourBackend router = (RoutingDetourBackend)DetourBackend.Current;
            router.Flush();

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

            RoutingDetourBackend router = (RoutingDetourBackend)DetourBackend.Current;
            Assert.Equal(RouteState.Raw, router.GetRoute(ProbeMethod()));

            FakeBridge lateBridge = new FakeBridge
            {
                ForeignOwnersFunc = _ => new List<string> { "late.owner" }.AsReadOnly()
            };
            lateBridge.Enqueue(BridgeRouteResult.Routed(new FakeHandle()));

            context.LoadBridge = (root, l) => lateBridge;

            RimWorldAdapter.TryLateActivation(context);
            router.Flush();

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
}
