using Concord;
using Verse;

namespace Concord.RimWorld.TestMod;

[Patch]
public abstract class TestPatch : Verse.Root {
    [Inject(At.Head, nameof(Verse.Root.Update))]
    public void ProbeUpdate(ControlHandle ch) {
        Log.Message("[Concord.RimWorld.TestMod] probe patch fired on Root.Update");
    }
}
