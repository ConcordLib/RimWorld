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

internal class FakeBridge : IHarmonyBridge
{
    private readonly Queue<BridgeRouteResult> results = new Queue<BridgeRouteResult>();

    public int TryRouteCallCount;
    public int ApplyToRoutedCallCount;
    public bool LastForceRoute;
    public Func<MethodBase, IReadOnlyList<string>> ForeignOwnersFunc;

    public void Enqueue(BridgeRouteResult result)
    {
        results.Enqueue(result);
    }

    public BridgeRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute)
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
