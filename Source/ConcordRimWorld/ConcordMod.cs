using System;
using System.Collections.Generic;
using Verse;

namespace Concord.RimWorld;

public class ConcordMod : Mod {
    public ConcordMod(ModContentPack content) : base(content) {
        try {
            CheckLoadOrder(content);
            RimWorldAdapter.Wire();
            Log.Message("[Concord.RimWorld] Concord runtime wired.");
        } catch (Exception e) {
            Log.Error("[Concord.RimWorld] Failed to initialize Concord: " + e);
        }
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
}
