using Concord;
using Verse;

namespace Concord.RimWorld.TestMod;

[Patch]
public abstract class TestPatch : Verse.Root {
    private static bool logged;

    [Inject(At.Head, nameof(Verse.Root.Update))]
    public void ProbeUpdate(ControlHandle ch) {
        if (logged) {
            return;
        }

        logged = true;
        Log.Message("[Concord.RimWorld.TestMod] probe patch fired on Root.Update");
        Log.Message("[Concord.RimWorld.TestMod] constant probe: Threshold() = " + TestMod.Threshold());
    }
}
