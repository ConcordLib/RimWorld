using Verse;

namespace Concord.RimWorld.TestMod;

public class TestMod : Mod {
    public TestMod(ModContentPack content) : base(content) {
        Concord.Patcher.Apply(typeof(TestMod).Assembly);
        Log.Message("[Concord.RimWorld.TestMod] test mod patches applied");
    }

    public static float Threshold() {
        return 18f;
    }
}
