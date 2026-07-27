using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

internal class FakeInner : IDetourBackend
{
    public int ApplyCallCount;
    public int ApplyComposedCallCount;
    public List<Tuple<MethodBase, IReadOnlyList<Injection>>> ApplyComposedCalls =
        new List<Tuple<MethodBase, IReadOnlyList<Injection>>>();
    public List<FakeHandle> ApplyComposedReturnedHandles = new List<FakeHandle>();

    public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
    {
        ApplyCallCount++;
        return new FakeHandle { Original = original };
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
    {
        ApplyComposedCallCount++;
        ApplyComposedCalls.Add(Tuple.Create(target, added));
        FakeHandle handle = new FakeHandle { Original = target };
        ApplyComposedReturnedHandles.Add(handle);
        return handle;
    }
}

internal class FakeBridge : IForeignPatchHost
{
    private readonly Queue<ForeignRouteResult> results = new Queue<ForeignRouteResult>();
    private readonly Queue<ForeignRouteResult> routeIntoResults = new Queue<ForeignRouteResult>();

    public int TryRouteCallCount;
    public int ApplyToRoutedCallCount;
    public int RouteIntoCallCount;
    public bool LastForceRoute;
    public object LastHostPatchState;
    public string ValidateRouteReason;
    public bool NotifierInstalls = true;
    public IForeignPatchObserver InstalledObserver;
    public Func<MethodBase, IReadOnlyList<string>> ForeignOwnersFunc;

    public void Enqueue(ForeignRouteResult result)
    {
        results.Enqueue(result);
    }

    public void EnqueueRouteInto(ForeignRouteResult result)
    {
        routeIntoResults.Enqueue(result);
    }

    public IDisposable EnterHostLock()
    {
        return null;
    }

    public string ValidateRoute(MethodBase target, IReadOnlyList<Injection> added)
    {
        return ValidateRouteReason;
    }

    public ForeignRouteResult RouteInto(MethodBase target, IReadOnlyList<Injection> added, object hostPatchState)
    {
        RouteIntoCallCount++;
        LastHostPatchState = hostPatchState;
        return routeIntoResults.Count > 0
            ? routeIntoResults.Dequeue()
            : ForeignRouteResult.Routed(new FakeHandle { Original = target });
    }

    public bool TryInstallNotifier(IForeignPatchObserver observer, IDetourBackend rawBackend)
    {
        InstalledObserver = observer;
        return NotifierInstalls;
    }

    public ForeignRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute)
    {
        TryRouteCallCount++;
        LastForceRoute = forceRoute;
        return results.Dequeue();
    }

    public IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added)
    {
        ApplyToRoutedCallCount++;
        return new FakeHandle { Original = target };
    }

    public IReadOnlyList<string> ForeignOwners(MethodBase target)
    {
        return ForeignOwnersFunc != null ? ForeignOwnersFunc(target) : Array.Empty<string>();
    }
}

internal class FakeHandle : IDetourHandle
{
    public MethodBase Original { get; set; }
    public bool IsApplied { get; set; } = true;
    public int DisposeCallCount;

    public void Dispose()
    {
        DisposeCallCount++;
        IsApplied = false;
    }
}
