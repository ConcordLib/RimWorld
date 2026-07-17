using Concord.Detour;

namespace Concord.RimWorld;

public sealed class BridgeRouteResult
{
    private BridgeRouteResult(BridgeRouteKind kind, IDetourHandle handle, string reason)
    {
        Kind = kind;
        Handle = handle;
        Reason = reason;
    }

    public BridgeRouteKind Kind { get; }
    public IDetourHandle Handle { get; }
    public string Reason { get; }

    public static BridgeRouteResult NotContested() => new(BridgeRouteKind.NotContested, null, null);

    public static BridgeRouteResult Routed(IDetourHandle handle) => new(BridgeRouteKind.Routed, handle, null);

    public static BridgeRouteResult Rejected(string reason) => new(BridgeRouteKind.Rejected, null, reason);
}
