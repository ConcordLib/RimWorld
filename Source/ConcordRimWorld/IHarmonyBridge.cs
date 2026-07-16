using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.RimWorld;

public interface IHarmonyBridge
{
    BridgeRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute);
    IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added);
    IReadOnlyList<string> ForeignOwners(MethodBase target);
}
