using Verse;

namespace Concord.RimWorld;

public sealed class ConcordSettings : ModSettings
{
    public bool BridgeRoutingEnabled = true;
    public bool RouteEverythingWhenHarmonyPresent;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref BridgeRoutingEnabled, "BridgeRoutingEnabled", true);
        Scribe_Values.Look(ref RouteEverythingWhenHarmonyPresent, "RouteEverythingWhenHarmonyPresent", false);
    }
}
