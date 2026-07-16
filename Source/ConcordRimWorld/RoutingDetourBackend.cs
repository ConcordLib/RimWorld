using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.RimWorld;

public sealed class RoutingDetourBackend : IDetourBackend
{
    private readonly object gate = new object();
    private readonly IDetourBackend inner;
    private readonly Action<string> log;
    private readonly Dictionary<MethodBase, RouteState> routes = new Dictionary<MethodBase, RouteState>();
    private readonly Dictionary<MethodBase, string> rejectionReasons = new Dictionary<MethodBase, string>();
    private readonly HashSet<MethodBase> rawInventory = new HashSet<MethodBase>();
    private IHarmonyBridge bridge;

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
                PinRaw(original);
            }

            return inner.Apply(original, replacement);
        }
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added)
    {
        target = MethodIdentity.Normalize(target);

        lock (gate)
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
                PinRaw(target);
                return inner.ApplyComposed(target, added);
            }

            BridgeRouteResult result = bridge.TryRoute(target, added, RouteEverything);

            if (result.Kind == BridgeRouteKind.NotContested)
            {
                PinRaw(target);
                return inner.ApplyComposed(target, added);
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
}
