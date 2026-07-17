using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration;
using Verse;

namespace Concord.RimWorld;

public static class RimWorldAdapter {
    private static bool wired;
    private static RimWorldPatchApplier patchApplier;
    private static volatile IHarmonyBridge activeBridge;
    private static Func<MethodBase, IReadOnlyList<string>> reflectionLookup;
    private static AssemblyLoadEventHandler lateActivationHandler;
    private static IDetourBackend backendBeforeWire;

    public static void Wire(ModContentPack content, ConcordSettings settings) {
        WireContext context = new WireContext {
            Settings = settings,
            ModRootDir = content.RootDir,
            Schedule = LongEventHandler.ExecuteWhenFinished,
            LoadBridge = HarmonyProbe.TryLoadBridge,
            Log = Log.Warning,
            DialogOnce = text => Find.WindowStack.Add(new Dialog_MessageBox(text)),
            ApplyEagerTier = () => {
                PropertyRegistry registry = new PropertyRegistry();
                RimWorldRuntime.Registry = registry;
                patchApplier = new RimWorldPatchApplier();

                RimWorldAttachedPropertyRegistry propertyRegistry = new RimWorldAttachedPropertyRegistry(registry, "Concord.RimWorld");
                PatchDeclarationScanner.ScanAssembly(typeof(RimWorldAdapter).Assembly, patchApplier, propertyRegistry);
            }
        };

        Wire(context);
    }

    internal static void Wire(WireContext context) {
        if (wired) {
            return;
        }

        wired = true;

        backendBeforeWire = DetourBackend.Current;
        RoutingDetourBackend router = new RoutingDetourBackend(backendBeforeWire, context.Log);
        DetourBackend.Current = router;

        router.RouteEverything = context.Settings.RouteEverythingWhenHarmonyPresent;

        using (router.EagerScope()) {
            context.ApplyEagerTier();
        }

        if (context.Settings.BridgeRoutingEnabled) {
            IHarmonyBridge bridge = context.LoadBridge(context.ModRootDir, context.Log);
            if (bridge != null) {
                activeBridge = bridge;
                router.ActivateBridge(bridge);
            }

            lateActivationHandler = (sender, args) => {
                if (activeBridge != null) {
                    return;
                }

                if (args.LoadedAssembly.GetName().Name != "0Harmony") {
                    return;
                }

                TryLateActivation(context);
            };

            AppDomain.CurrentDomain.AssemblyLoad += lateActivationHandler;
        }

        Func<MethodBase, IReadOnlyList<string>> lookup = target => {
            IHarmonyBridge bridge = activeBridge;
            if (bridge != null) {
                return bridge.ForeignOwners(target);
            }

            reflectionLookup ??= ReflectionHarmonyObserver.TryCreateForeignOwnerLookup(() => AppDomain.CurrentDomain.GetAssemblies(), context.Log);
            return reflectionLookup != null ? reflectionLookup(target) : Array.Empty<string>();
        };

        ContentionWatcher watcher = new ContentionWatcher(router.RawPinnedTargets, lookup, context.Log, context.DialogOnce);

        context.Schedule(() => {
            watcher.RunCheckpoint();
            context.Schedule(() => {
                router.Flush();
                watcher.RunCheckpoint();
            });
        });
    }

    internal static void TryLateActivation(WireContext context) {
        IHarmonyBridge bridge = context.LoadBridge(context.ModRootDir, context.Log);
        if (bridge == null) {
            return;
        }

        activeBridge = bridge;

        if (DetourBackend.Current is RoutingDetourBackend router) {
            router.ActivateBridge(bridge);
            if (lateActivationHandler != null) {
                AppDomain.CurrentDomain.AssemblyLoad -= lateActivationHandler;
                lateActivationHandler = null;
            }
        }
    }

    internal static void ResetForTests() {
        wired = false;
        patchApplier = null;
        activeBridge = null;
        reflectionLookup = null;

        if (lateActivationHandler != null) {
            AppDomain.CurrentDomain.AssemblyLoad -= lateActivationHandler;
            lateActivationHandler = null;
        }

        if (backendBeforeWire != null) {
            DetourBackend.Current = backendBeforeWire;
            backendBeforeWire = null;
        }
    }
}

internal sealed class WireContext {
    public ConcordSettings Settings;
    public string ModRootDir;
    public Action<Action> Schedule;
    public Func<string, Action<string>, IHarmonyBridge> LoadBridge;
    public Action<string> Log;
    public Action<string> DialogOnce;
    public Action ApplyEagerTier;
}
