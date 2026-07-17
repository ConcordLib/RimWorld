#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Emit;
using HarmonyLib;

namespace Concord.RimWorld.Harmony
{
    internal static class SupportMatrix
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodeTable();

        internal static string Validate(MethodBase target, IReadOnlyList<Injection> added, Patches patchInfo)
        {
            if (target.IsConstructor)
            {
                foreach (Injection injection in added)
                {
                    if (injection.At is InjectAt.Around)
                    {
                        return $"Constructor {target.Name} cannot receive whole-method Around patch (uninitialized this under Harmony)";
                    }
                }
            }

            if (HasInnerPatches(patchInfo))
            {
                return $"Target {target.Name} has Harmony 2.4 inner patches (not composable with Concord detours)";
            }

            MethodBase canonical = WrapperComposer.ResolveStateMachineTarget(target);
            if (canonical != target)
            {
                return $"Async/iterator entry method {target.Name} is not the canonical target (stream source is the state machine method)";
            }

            try
            {
                WrapperComposer.RejectSharedGenericInstantiation(target);
            }
            catch (ConcordEmitException ex)
            {
                return $"Target {target.Name} is a shared reference-type generic instantiation: {ex.Message}";
            }

            foreach (Injection injection in added)
            {
                if (CallsGetExecutingAssembly(injection.InjectionMethod))
                {
                    return $"Injection method {injection.InjectionMethod.Name} calls Assembly.GetExecutingAssembly (Harmony rewrites this to target's assembly, breaking observation)";
                }
            }

            return null;
        }

        internal static bool HasInnerPatches(Patches patchInfo)
        {
            if (patchInfo == null)
            {
                return false;
            }

            FieldInfo innerPrefixesField = typeof(Patches).GetField("InnerPrefixes", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo innerPostfixesField = typeof(Patches).GetField("InnerPostfixes", BindingFlags.Public | BindingFlags.Instance);

            if (innerPrefixesField != null)
            {
                object innerPrefixes = innerPrefixesField.GetValue(patchInfo);
                if (innerPrefixes is System.Collections.ICollection prefixCollection && prefixCollection.Count > 0)
                {
                    return true;
                }
            }

            if (innerPostfixesField != null)
            {
                object innerPostfixes = innerPostfixesField.GetValue(patchInfo);
                if (innerPostfixes is System.Collections.ICollection postfixCollection && postfixCollection.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool CallsGetExecutingAssembly(MethodBase method)
        {
            MethodBody body = method.GetMethodBody();
            if (body == null)
            {
                return false;
            }

            byte[] il = body.GetILAsByteArray();
            Module module = method.Module;
            int position = 0;
            while (position < il.Length)
            {
                short value = il[position];
                position++;
                if (value == 0xFE)
                {
                    value = (short)(0xFE00 | il[position]);
                    position++;
                }

                OpCode opcode = OpCodesByValue[value];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    int token = BitConverter.ToInt32(il, position);
                    MethodBase resolved;
                    try
                    {
                        resolved = module.ResolveMethod(token, method.DeclaringType?.GetGenericArguments(), method is MethodInfo genericSource ? genericSource.GetGenericArguments() : null);
                    }
                    catch (ArgumentException)
                    {
                        resolved = null;
                    }

                    if (resolved != null && resolved.Name == "GetExecutingAssembly" && resolved.DeclaringType == typeof(System.Reflection.Assembly))
                    {
                        return true;
                    }
                }

                position += OperandSize(opcode, il, position);
            }

            return false;
        }

        private static Dictionary<short, OpCode> BuildOpCodeTable()
        {
            Dictionary<short, OpCode> table = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode code)
                {
                    table[code.Value] = code;
                }
            }

            return table;
        }

        private static int OperandSize(OpCode opcode, byte[] il, int position)
        {
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    return 4 + (BitConverter.ToInt32(il, position) * 4);
                default:
                    return 4;
            }
        }
    }
}
