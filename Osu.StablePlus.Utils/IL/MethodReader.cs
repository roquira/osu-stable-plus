using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Osu.StablePlus.Utils.IL;

public static class MethodReader
{
    public static IEnumerable<IlInstruction> GetInstructions(MethodBase method)
    {
        if (IlCache.Current is { } cache)
            return cache.Bodies.GetOrAdd(method, m => new Body(Read(m))).Instructions();

        return Read(method).Select(inst => new IlInstruction { Opcode = inst.Key, Operand = inst.Value });
    }

    /// <summary>
    ///     Whether any instruction's operand equals <paramref name="operand" />, exactly as
    ///     <c>GetInstructions(method).Any(i => Equals(i.Operand, operand))</c>. Numbers are boxed as their
    ///     instruction encodes them: <see cref="double" /> for <c>ldc.r8</c> or <see cref="sbyte" /> for <c>ldc.i4.s</c>.
    ///     Within an <see cref="IlCache" /> scope, members, strings and numbers are found from the method's
    ///     metadata tokens and constants without reading its instructions.
    /// </summary>
    public static bool References(MethodBase method, object operand)
    {
        if (IlCache.Current is { } cache && operand is MemberInfo or string or sbyte or byte or int or long or float or double &&
            cache.Operands.GetOrAdd(method, Operands.Read) is { } operands)
            return operands.Contains(operand, cache);

        return GetInstructions(method).Any(inst => Equals(inst.Operand, operand));
    }

    // Harmony's method body reader, without an ILGenerator to declare locals and labels in.
    private static IEnumerable<KeyValuePair<OpCode, object>> Read(MethodBase method) =>
        PatchProcessor.ReadMethodBody(method, null);

    /// <summary>
    ///     A compact copy of a method body's instructions: opcodes are stored by value.
    /// </summary>
    internal sealed class Body
    {
        private readonly short[] opcodes;
        private readonly object[] operands;

        internal Body(IEnumerable<KeyValuePair<OpCode, object>> instructions)
        {
            var list = instructions.ToList();
            opcodes = list.Select(inst => inst.Key.Value).ToArray();
            operands = list.Select(inst => inst.Value).ToArray();
        }

        internal IEnumerable<IlInstruction> Instructions()
        {
            for (var index = 0; index < opcodes.Length; index++)
                yield return new IlInstruction { Opcode = OpCodeReader.FromValue(opcodes[index]), Operand = operands[index] };
        }
    }

    /// <summary>
    ///     The unresolved member, string and numeric operands of a method body.
    /// </summary>
    internal sealed class Operands
    {
        private readonly Module module;
        private readonly int[] members;
        private readonly int[] strings;
        private readonly HashSet<object> constants;

        private Operands(Module module, int[] members, int[] strings, HashSet<object> constants)
        {
            this.module = module;
            this.members = members;
            this.strings = strings;
            this.constants = constants;
        }

        /// <summary>
        ///     Reads a method's operands, or null when Harmony's reader must be used instead: tokens in generic
        ///     methods and types are resolved in their generic context, and methods without IL get special handling.
        /// </summary>
        internal static Operands? Read(MethodBase method)
        {
            if (method.IsGenericMethod || method.DeclaringType is { IsGenericType: true }) return null;
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null || il.Length == 0) return null;

            var members = new HashSet<int>();
            var strings = new HashSet<int>();
            var constants = new HashSet<object>();
            foreach (var instruction in new OpCodeReader(il).GetOperandOffsets())
            {
                var at = instruction.Value;
                switch (instruction.Key.OperandType)
                {
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        members.Add(BitConverter.ToInt32(il, at));
                        break;
                    case OperandType.InlineString:
                        strings.Add(BitConverter.ToInt32(il, at));
                        break;
                    // Boxed with the same types as Harmony's reader.
                    case OperandType.ShortInlineI:
                        constants.Add(instruction.Key == OpCodes.Ldc_I4_S ? (object)(sbyte)il[at] : il[at]);
                        break;
                    case OperandType.InlineI:
                        constants.Add(BitConverter.ToInt32(il, at));
                        break;
                    case OperandType.InlineI8:
                        constants.Add(BitConverter.ToInt64(il, at));
                        break;
                    case OperandType.ShortInlineR:
                        constants.Add(BitConverter.ToSingle(il, at));
                        break;
                    case OperandType.InlineR:
                        constants.Add(BitConverter.ToDouble(il, at));
                        break;
                }
            }

            return new Operands(method.Module, members.ToArray(), strings.ToArray(), constants);
        }

        internal bool Contains(object operand, IlCache.Scope cache) => operand switch
        {
            MemberInfo member => members.Any(token => Equals(cache.Resolve(module, token), member)),
            string text => strings.Any(token => module.ResolveString(token) == text),
            _ => constants.Contains(operand),
        };
    }
}

public struct IlInstruction
{
    public OpCode Opcode;
    public object Operand;

    public override string ToString() => $"ILInstruction({Opcode}" + (Operand != null ? $", {Operand})" : ")");
}
