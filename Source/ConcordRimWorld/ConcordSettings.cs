using Verse;

namespace Concord.RimWorld;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1104", Justification = "Scribe_Values.Look and CheckboxLabeled take these by ref, and a property cannot be passed by ref.")]
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
