using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration;
using Verse;

namespace Concord.RimWorld;

public static class RimWorldAdapter {
    private static bool wired;
    private static RimWorldPatchApplier patchApplier;
    private static volatile IForeignPatchHost activeBridge;
    private static Func<MethodBase, IReadOnlyList<string>> reflectionLookup;
    private static AssemblyLoadEventHandler lateActivationHandler;
    private static int lateActivationAttempted;
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

        context.ApplyEagerTier();

        if (context.Settings.BridgeRoutingEnabled) {
            IForeignPatchHost bridge = context.LoadBridge(context.ModRootDir, context.Log);
            if (bridge != null) {
                activeBridge = bridge;
                router.ActivateHost(bridge);
            } else {
                lateActivationHandler = (sender, args) => {
                    if (activeBridge != null) {
                        if (lateActivationHandler != null) {
                            AppDomain.CurrentDomain.AssemblyLoad -= lateActivationHandler;
                            lateActivationHandler = null;
                        }

                        return;
                    }

                    if (args.LoadedAssembly.GetName().Name != "0Harmony") {
                        return;
                    }

                    TryLateActivation(context);
                };

                AppDomain.CurrentDomain.AssemblyLoad += lateActivationHandler;
            }
        }

        Func<MethodBase, IReadOnlyList<string>> lookup = target => {
            IForeignPatchHost bridge = activeBridge;
            if (bridge != null) {
                return bridge.ForeignOwners(target);
            }

            reflectionLookup ??= ReflectionHarmonyObserver.TryCreateForeignOwnerLookup(() => AppDomain.CurrentDomain.GetAssemblies(), context.Log);
            return reflectionLookup != null ? reflectionLookup(target) : Array.Empty<string>();
        };

        ContentionWatcher watcher = new ContentionWatcher(
            router.RawPinnedTargets,
            router.ContestedLostTargets,
            lookup,
            () => router.NotifierInstalled,
            context.Log,
            context.DialogOnce);

        // Patches now apply as they arrive, so there is no queue to drain. The watcher stays as a
        // backstop for the case where the notifier hook could not install.
        context.Schedule(() => {
            watcher.RunCheckpoint();
            context.Schedule(watcher.RunCheckpoint);
        });
    }

    internal static void TryLateActivation(WireContext context) {
        if (Interlocked.CompareExchange(ref lateActivationAttempted, 1, 0) != 0) {
            return;
        }

        try {
            IForeignPatchHost bridge = context.LoadBridge(context.ModRootDir, context.Log);
            if (bridge == null) {
                return;
            }

            activeBridge = bridge;

            if (DetourBackend.Current is RoutingDetourBackend router) {
                router.ActivateHost(bridge);
            }
        }
        finally {
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
        lateActivationAttempted = 0;

        if (lateActivationHandler != null) {
            AppDomain.CurrentDomain.AssemblyLoad -= lateActivationHandler;
            lateActivationHandler = null;
        }

        if (backendBeforeWire != null) {
            DetourBackend.Current = backendBeforeWire;
            backendBeforeWire = null;
        }
    }

    internal static bool HasLateActivationHandler => lateActivationHandler != null;
}

internal sealed class WireContext {
    public ConcordSettings Settings;
    public string ModRootDir;
    public Action<Action> Schedule;
    public Func<string, Action<string>, IForeignPatchHost> LoadBridge;
    public Action<string> Log;
    public Action<string> DialogOnce;
    public Action ApplyEagerTier;
}
