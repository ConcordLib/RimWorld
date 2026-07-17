using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Concord.RimWorld;
using HarmonyLib;

namespace Concord.RimWorld.Harmony;

public sealed class HarmonyBridge : IHarmonyBridge
{
    private readonly HarmonyLib.Harmony harmony;
    private readonly Action<string> log;
    private bool lockUnavailableLogged;

    internal Action<MethodBase> InstallParticipant;
    internal Action<MethodBase> RemoveParticipant;

    public HarmonyBridge(Action<string> log)
    {
        this.log = log;
        harmony = new HarmonyLib.Harmony("concord.bridge");
        TranspilerParticipant.Log = log;

        InstallParticipant = target => harmony.Patch(target, transpiler: new HarmonyMethod(TranspilerParticipant.TranspileMethod) { priority = Priority.Last });
        RemoveParticipant = target => harmony.Unpatch(target, TranspilerParticipant.TranspileMethod);
    }

    public BridgeRouteResult TryRoute(MethodBase target, IReadOnlyList<Injection> added, bool forceRoute)
    {
        target = MethodIdentity.Normalize(target);

        if (!HarmonyLockScope.Available)
        {
            if (!lockUnavailableLogged)
            {
                lockUnavailableLogged = true;
                log("PatchProcessor.locker not found - routing raw with watchdog coverage; contested-check race admitted");
            }

            return BridgeRouteResult.NotContested();
        }

        using (HarmonyLockScope.Enter())
        {
            Patches patchInfo = PatchProcessor.GetPatchInfo(target);
            bool foreign = HasForeignPatch(patchInfo);

            if (!foreign && !forceRoute)
            {
                return BridgeRouteResult.NotContested();
            }

            string reason = SupportMatrix.Validate(target, added, patchInfo);
            if (reason != null)
            {
                return BridgeRouteResult.Rejected(reason);
            }

            long[] owned = TranspilerParticipant.Registry.Add(target, added);
            Exception failure;
            try
            {
                failure = RunRebuild(target);
            }
            catch
            {
                AbandonFirstApplication(target, owned);
                throw;
            }

            if (failure != null)
            {
                AbandonFirstApplication(target, owned);
                return BridgeRouteResult.Rejected(failure.Message);
            }

            return BridgeRouteResult.Routed(new BridgeDetourHandle(this, target, owned));
        }
    }

    public IDetourHandle ApplyToRouted(MethodBase target, IReadOnlyList<Injection> added)
    {
        target = MethodIdentity.Normalize(target);

        long[] owned;

        if (HarmonyLockScope.Available)
        {
            using (HarmonyLockScope.Enter())
            {
                Patches patchInfo = PatchProcessor.GetPatchInfo(target);
                string reason = SupportMatrix.Validate(target, added, patchInfo);
                if (reason != null)
                {
                    throw new InvalidOperationException(reason);
                }

                owned = TranspilerParticipant.Registry.Add(target, added);
                Exception failure;
                try
                {
                    failure = RunRebuild(target);
                }
                catch
                {
                    RecoverSurvivors(target, owned);
                    throw;
                }

                if (failure != null)
                {
                    RecoverSurvivors(target, owned);
                    throw new InvalidOperationException(failure.Message);
                }
            }
        }
        else
        {
            Patches patchInfo = PatchProcessor.GetPatchInfo(target);
            string reason = SupportMatrix.Validate(target, added, patchInfo);
            if (reason != null)
            {
                throw new InvalidOperationException(reason);
            }

            owned = TranspilerParticipant.Registry.Add(target, added);
            Exception failure;
            try
            {
                failure = RunRebuild(target);
            }
            catch
            {
                RecoverSurvivors(target, owned);
                throw;
            }

            if (failure != null)
            {
                RecoverSurvivors(target, owned);
                throw new InvalidOperationException(failure.Message);
            }
        }

        return new BridgeDetourHandle(this, target, owned);
    }

    public IReadOnlyList<string> ForeignOwners(MethodBase target)
    {
        target = MethodIdentity.Normalize(target);

        Patches patchInfo = PatchProcessor.GetPatchInfo(target);
        if (patchInfo == null)
        {
            return Array.Empty<string>();
        }

        HashSet<string> owners = new HashSet<string>();
        CollectForeignOwners(patchInfo.Prefixes, owners);
        CollectForeignOwners(patchInfo.Postfixes, owners);
        CollectForeignOwners(patchInfo.Transpilers, owners);
        CollectForeignOwners(patchInfo.Finalizers, owners);
        CollectForeignOwners(patchInfo.InnerPrefixes, owners);
        CollectForeignOwners(patchInfo.InnerPostfixes, owners);

        return new List<string>(owners);
    }

    internal void DisposeHandle(MethodBase target, long[] owned)
    {
        if (HarmonyLockScope.Available)
        {
            using (HarmonyLockScope.Enter())
            {
                DisposeHandleLocked(target, owned);
            }

            return;
        }

        DisposeHandleLocked(target, owned);
    }

    private void DisposeHandleLocked(MethodBase target, long[] owned)
    {
        (long Seq, Injection Injection)[] removed = TranspilerParticipant.Registry.Remove(target, owned);

        Exception failure;
        try
        {
            failure = RunRebuild(target);
        }
        catch
        {
            TranspilerParticipant.Registry.Restore(target, removed);

            try
            {
                Exception restoreFailure = RunRebuild(target);
                if (restoreFailure != null)
                {
                    LoseParticipant(target, restoreFailure);
                }
            }
            catch (Exception restoreEx)
            {
                LoseParticipant(target, restoreEx);
            }

            throw;
        }

        if (failure != null)
        {
            LoseParticipant(target, failure);
        }
    }

    private void LoseParticipant(MethodBase target, Exception failure)
    {
        TranspilerParticipant.Registry.Clear(target);

        try
        {
            RemoveParticipant(target);
        }
        catch (Exception ex)
        {
            log("removing the concord participant failed for " + target.Name + ": " + ex.Message);
        }

        log("bridge participant lost for " + target.Name + "; its Concord injections are disabled: " + failure.Message);
    }

    private void RecoverSurvivors(MethodBase target, long[] droppedOwned)
    {
        TranspilerParticipant.Registry.Remove(target, droppedOwned);
        Exception survivorFailure;
        try
        {
            survivorFailure = RunRebuild(target);
        }
        catch (Exception ex)
        {
            survivorFailure = ex;
        }

        if (survivorFailure != null)
        {
            LoseParticipant(target, survivorFailure);
        }
    }

    private void AbandonFirstApplication(MethodBase target, long[] owned)
    {
        TranspilerParticipant.Registry.Remove(target, owned);

        try
        {
            RemoveParticipant(target);
        }
        catch
        {
        }
    }

    private void ReconcileParticipant(MethodBase target)
    {
        RemoveParticipant(target);
        if (TranspilerParticipant.Registry.HasInjections(target))
        {
            InstallParticipant(target);
        }
    }

    private Exception RunRebuild(MethodBase target)
    {
        TranspilerParticipant.LastStreamFailure = null;
        ReconcileParticipant(target);
        Exception failure = TranspilerParticipant.LastStreamFailure;
        TranspilerParticipant.LastStreamFailure = null;
        return failure;
    }

    private static bool HasForeignPatch(Patches patchInfo)
    {
        if (patchInfo == null)
        {
            return false;
        }

        if (patchInfo.InnerPrefixes.Count > 0 || patchInfo.InnerPostfixes.Count > 0)
        {
            return true;
        }

        return HasForeignEntry(patchInfo.Prefixes) ||
               HasForeignEntry(patchInfo.Postfixes) ||
               HasForeignEntry(patchInfo.Transpilers) ||
               HasForeignEntry(patchInfo.Finalizers);
    }

    private static bool HasForeignEntry(IReadOnlyList<Patch> patches)
    {
        for (int i = 0; i < patches.Count; i++)
        {
            if (patches[i].PatchMethod != TranspilerParticipant.TranspileMethod)
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectForeignOwners(IReadOnlyList<Patch> patches, HashSet<string> owners)
    {
        for (int i = 0; i < patches.Count; i++)
        {
            if (patches[i].PatchMethod != TranspilerParticipant.TranspileMethod)
            {
                owners.Add(patches[i].owner);
            }
        }
    }
}
