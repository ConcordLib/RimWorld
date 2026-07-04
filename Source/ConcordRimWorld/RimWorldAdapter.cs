using System.Reflection;
using Concord.Emit;
using Concord.Orchestration;
using Verse;

namespace Concord.RimWorld;

public static class RimWorldAdapter {
    private static bool wired;
    private static RimWorldPatchApplier patchApplier;

    public static void Wire() {
        if (wired) {
            return;
        }

        wired = true;
        PropertyRegistry registry = new PropertyRegistry();
        RimWorldRuntime.Registry = registry;
        patchApplier = new RimWorldPatchApplier();

        RimWorldAttachedPropertyRegistry propertyRegistry = new RimWorldAttachedPropertyRegistry(registry, "Concord.RimWorld");
        PatchDeclarationScanner.ScanAssembly(typeof(RimWorldAdapter).Assembly, patchApplier, propertyRegistry);

        ApplyDefFromNodePatch();
    }

    private static void ApplyDefFromNodePatch() {
        if (RimWorldRuntime.Registry.IsEmpty) {
            return;
        }

        MethodInfo target = typeof(DirectXmlLoader).GetMethod(
            nameof(DirectXmlLoader.DefFromNode),
            BindingFlags.Public | BindingFlags.Static);
        MethodInfo injectionMethod = typeof(DefFromNodePatch).GetMethod(nameof(DefFromNodePatch.WrapDefFromNode));
        Injection injection = new Injection(injectionMethod, new InjectAt.Around(), typeof(DefFromNodePatch).FullName, 0);
        patchApplier.ApplyPatch(target, injection);
    }
}
