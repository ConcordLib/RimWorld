using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Concord.RimWorld;

public class ConcordMod : Mod {
    public ConcordMod(ModContentPack content) : base(content) {
        if (ShippedConcordCannotLoad()) {
            Log.Error(
                "[Concord.RimWorld] The shipped Concord runtime depends on System.Reflection.Emit " +
                "facade assemblies that this runtime does not provide. Concord wiring is skipped; " +
                "mods depending on Concord will not be patched. A runtime-compatible Concord build " +
                "is required.");
            return;
        }

        try {
            CheckLoadOrder(content);
            ConcordSettings settings = GetSettings<ConcordSettings>();
            RimWorldAdapter.Wire(content, settings);
            Log.Message("[Concord.RimWorld] Concord runtime wired.");
        } catch (Exception e) {
            Log.Error("[Concord.RimWorld] Failed to initialize Concord: " + e);
        }
    }

    private static bool ShippedConcordCannotLoad() {
        Assembly concord = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.GetName().Name == "Concord") {
                concord = assembly;
                break;
            }
        }

        if (concord == null) {
            return false;
        }

        bool referencesFacade = concord.GetReferencedAssemblies().Any(reference =>
            reference.Name == "System.Reflection.Emit.Lightweight" ||
            reference.Name == "System.Reflection.Emit.ILGeneration");

        if (!referencesFacade) {
            return false;
        }

        return Type.GetType(
            "System.Reflection.Emit.DynamicMethod, System.Reflection.Emit.Lightweight, " +
            "Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            false) == null;
    }

    private static void CheckLoadOrder(ModContentPack content) {
        string selfId = content.PackageId;
        string coreId = ModContentPack.CoreModPackageId;
        List<string> before = [];
        bool coreBefore = false;
        foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder) {
            if (mod == null) {
                continue;
            }

            string id = mod.PackageId;
            if (string.Equals(id, selfId, StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            if (string.Equals(id, coreId, StringComparison.OrdinalIgnoreCase)) {
                coreBefore = true;
            }

            before.Add(id);
        }

        if (coreBefore) {
            Log.Error(
                "[Concord.RimWorld] LOAD ORDER ERROR: core (" + coreId + ") loads before Concord. " +
                "Concord loads the patching runtime and MUST load before core and every other mod. " +
                "Move " + selfId + " to the very top of the mod list. Mods loaded before Concord: " +
                string.Join(", ", before));
            return;
        }

        if (before.Count > 0) {
            Log.Warning(
                "[Concord.RimWorld] LOAD ORDER WARNING: " + before.Count + " mod(s) load before Concord (" +
                string.Join(", ", before) + "). Concord should load first so its patching runtime is " +
                "available to every other mod. Move " + selfId + " to the top of the mod list.");
        }
    }

    public override string SettingsCategory()
    {
        return "Concord";
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        ConcordSettings settings = GetSettings<ConcordSettings>();
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.CheckboxLabeled("Bridge Routing Enabled", ref settings.BridgeRoutingEnabled,
            "Controls whether Concord's routing bridge is activated (the router always installs)");
        listing.CheckboxLabeled("Route Everything When Harmony Present", ref settings.RouteEverythingWhenHarmonyPresent,
            "Opt-in compat mode: route all methods when Harmony is detected");

        listing.Gap();
        listing.Label("Changes to these settings take effect on the next game launch.");

        listing.End();
    }
}
