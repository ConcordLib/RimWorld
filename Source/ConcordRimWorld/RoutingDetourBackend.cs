using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.RimWorld;

public sealed class RoutingDetourBackend : IDetourBackend
{
    [ThreadStatic]
    private static int eagerDepth;

    private readonly object gate = new object();
    private readonly IDetourBackend inner;
    private readonly Action<string> log;
    private readonly Dictionary<MethodBase, RouteState> routes = new Dictionary<MethodBase, RouteState>();
    private readonly Dictionary<MethodBase, string> rejectionReasons = new Dictionary<MethodBase, string>();
    private readonly HashSet<MethodBase> rawInventory = new HashSet<MethodBase>();
    private readonly Queue<PendingApply> pendingApplies = new Queue<PendingApply>();
    private IHarmonyBridge bridge;
    private bool flushed;

    public RoutingDetourBackend(IDetourBackend inner, Action<string> log)
    {
        this.inner = inner;
        this.log = log;
    }

    public bool RouteEverything { get; set; }

    public void ActivateBridge(IHarmonyBridge bridge)
    {
        lock (gate)
        {
            if (this.bridge != null)
            {
                return;
            }

            this.bridge = bridge;
        }
    }

    public IDetourHandle Apply(MethodBase original, MethodInfo replacement)
    {
        original = MethodIdentity.Normalize(original);

        lock (gate)
        {
            RouteState state = routes.TryGetValue(original, out RouteState existing) ? existing : RouteState.Unpinned;

            if (state == RouteState.Bridge)
            {
                throw new InvalidOperationException(
                    "Concord.RimWorld.RoutingDetourBackend.Apply is not coexistence-aware and cannot be used on a target routed to the Harmony bridge: " +
                    DescribeTarget(original)
                );
            }

            if (state == RouteState.Rejected)
            {
                throw new InvalidOperationException(rejectionReasons[original]);
            }

            if (state == RouteState.Unpinned)
            {
                IDetourHandle handle = inner.Apply(original, replacement);
                PinRaw(original);
                return handle;
            }

            return inner.Apply(original, replacement);
        }
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
        {
            if (flushed || eagerDepth > 0)
            {
                return ApplyComposedRouted(target, added);
            }

            PendingHandle pending = new PendingHandle(this, target);
            pendingApplies.Enqueue(new PendingApply(target, added, pending));
            return pending;
        }
    }

    public IDisposable EagerScope()
    {
        eagerDepth++;
        return new EagerScopeHandle();
    }

    public void Flush()
    {
        lock (gate)
        {
            if (flushed)
            {
                return;
            }

            flushed = true;

            int applied = 0;
            int failed = 0;

            while (pendingApplies.Count > 0)
            {
                PendingApply item = pendingApplies.Dequeue();

                if (item.Handle.Cancelled)
                {
                    continue;
                }

                try
                {
                    IDetourHandle real = ApplyComposedRouted(item.Target, item.Added);
                    item.Handle.Attach(real);
                    applied++;
                }
                catch (Exception ex)
                {
                    log("[Concord.Coex] deferred apply failed for " + DescribeTarget(item.Target) + ": " + ex.Message);
                    failed++;
                }
            }

            log(CoexistenceLogMarkers.FlushComplete + " applied=" + applied + " failed=" + failed);
        }
    }

    private IDetourHandle ApplyComposedRouted(MethodBase target, IReadOnlyList<Injection> added)
    {
        RouteState state = routes.TryGetValue(target, out RouteState existing) ? existing : RouteState.Unpinned;

        if (state == RouteState.Raw)
        {
            return inner.ApplyComposed(target, added);
        }

        if (state == RouteState.Bridge)
        {
            return bridge.ApplyToRouted(target, added);
        }

        if (state == RouteState.Rejected)
        {
            throw new InvalidOperationException(rejectionReasons[target]);
        }

        if (bridge == null)
        {
            IDetourHandle handle = inner.ApplyComposed(target, added);
            PinRaw(target);
            return handle;
        }

        BridgeRouteResult result = bridge.TryRoute(target, added, RouteEverything);

        if (result.Kind == BridgeRouteKind.NotContested)
        {
            IDetourHandle handle = inner.ApplyComposed(target, added);
            PinRaw(target);
            return handle;
        }

        if (result.Kind == BridgeRouteKind.Routed)
        {
            routes[target] = RouteState.Bridge;
            log(CoexistenceLogMarkers.RoutedContested + " " + DescribeTarget(target));
            return result.Handle;
        }

        routes[target] = RouteState.Rejected;
        rejectionReasons[target] = result.Reason;
        log(result.Reason);
        throw new InvalidOperationException(result.Reason);
    }

    internal RouteState GetRoute(MethodBase target)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
        {
            return routes.TryGetValue(target, out RouteState existing) ? existing : RouteState.Unpinned;
        }
    }

    internal IReadOnlyCollection<MethodBase> RawPinnedTargets()
    {
        lock (gate)
        {
            return new List<MethodBase>(rawInventory);
        }
    }

    private void PinRaw(MethodBase target)
    {
        routes[target] = RouteState.Raw;
        rawInventory.Add(target);
    }

    private static string DescribeTarget(MethodBase target)
    {
        return target.DeclaringType.Name + "." + target.Name;
    }

    private sealed class PendingApply
    {
        public readonly MethodBase Target;
        public readonly IReadOnlyList<Injection> Added;
        public readonly PendingHandle Handle;

        public PendingApply(MethodBase target, IReadOnlyList<Injection> added, PendingHandle handle)
        {
            Target = target;
            Added = added;
            Handle = handle;
        }
    }

    private sealed class PendingHandle : IDetourHandle
    {
        private readonly RoutingDetourBackend owner;
        private readonly MethodBase target;
        private IDetourHandle real;
        private bool disposed;

        public PendingHandle(RoutingDetourBackend owner, MethodBase target)
        {
            this.owner = owner;
            this.target = target;
        }

        public bool Cancelled { get; private set; }

        public MethodBase Original
        {
            get { return target; }
        }

        public bool IsApplied
        {
            get
            {
                lock (owner.gate)
                {
                    return real != null && real.IsApplied;
                }
            }
        }

        public void Attach(IDetourHandle handle)
        {
            real = handle;

            if (disposed)
            {
                real.Dispose();
            }
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;

                if (real != null)
                {
                    real.Dispose();
                }
                else
                {
                    Cancelled = true;
                }
            }
        }
    }

    private sealed class EagerScopeHandle : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            eagerDepth--;
        }
    }
}
