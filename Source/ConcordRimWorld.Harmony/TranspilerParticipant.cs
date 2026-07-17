using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using HarmonyLib;

namespace Concord.RimWorld.Harmony;

internal static class TranspilerParticipant
{
    internal static readonly BridgeTargetRegistry Registry = new BridgeTargetRegistry();

    internal static Action<string> Log;

    [ThreadStatic]
    internal static Exception LastStreamFailure;

    internal static readonly MethodInfo TranspileMethod = typeof(TranspilerParticipant).GetMethod(nameof(Transpile), BindingFlags.NonPublic | BindingFlags.Static);

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase original, ILGenerator generator)
    {
        if (original == null)
        {
            return instructions;
        }

        Injection[] ordered = Registry.OrderedSnapshot(MethodIdentity.Normalize(original));
        if (ordered.Length == 0)
        {
            return instructions;
        }

        List<CodeInstruction> stream = new List<CodeInstruction>(instructions);
        try
        {
            NeutralBody incoming = CodeInstructionConverter.ToNeutral(stream, out HarmonyStreamContext context);
            NeutralBody outgoing = BodyTransformer.Transform(original, incoming, ordered);
            return CodeInstructionConverter.FromNeutral(outgoing, context, generator);
        }
        catch (NeutralConversionException ex)
        {
            LastStreamFailure = ex;
            Log?.Invoke(CoexistenceLogMarkers.StreamRejected + " " + original.Name + ": " + ex.Message);
            return instructions;
        }
        catch (ConcordEmitException ex)
        {
            LastStreamFailure = ex;
            Log?.Invoke(CoexistenceLogMarkers.StreamRejected + " " + original.Name + ": " + ex.Message);
            return instructions;
        }
    }
}
