using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace Osu.StablePlus.Utils.Extensions;

/// <summary>
///     Utilities for patching bytecode through the usage of <see cref="HarmonyTranspiler" />.
/// </summary>
public static class TranspilerExtensions
{
    /// <summary>
    ///     Finds a certain IL signature and inserts instructions after it. This only matches the signature once.
    /// </summary>
    /// <param name="instructions">The input instructions to patch, mainly coming from a HarmonyTranspiler.</param>
    /// <param name="signature">The signature to find within the instructions based on IL opcodes.</param>
    /// <param name="newInstructions">The instructions to insert after a signature match.</param>
    public static IEnumerable<CodeInstruction> InsertAfterSignature(
        this IEnumerable<CodeInstruction> instructions,
        IReadOnlyList<OpCode> signature,
        IReadOnlyList<CodeInstruction> newInstructions)
    {
        RequireSignature(signature);
        var allInstructions = instructions.ToArray();
        var found = false;
        for (var index = 0; index < allInstructions.Length; index++)
        {
            if (!found && MatchesAt(allInstructions, index, signature))
            {
                found = true;
                for (var offset = 0; offset < signature.Count; offset++)
                    yield return allInstructions[index++];
                foreach (var newInstruction in newInstructions)
                    yield return newInstruction;
                index--;
            }
            else yield return allInstructions[index];
        }

        if (!found)
            throw new InvalidOperationException("Could not find the target signature in method!");
    }

    /// <summary>
    ///     Finds a certain IL signature and inserts instructions before it.
    /// </summary>
    /// <param name="instructions">The input instructions to patch, mainly coming from a HarmonyTranspiler.</param>
    /// <param name="signature">The signature to find within the instructions based on IL opcodes.</param>
    /// <param name="newInstructions">The instructions to insert before a signature match.</param>
    /// <param name="all">Match all instances of a signature.</param>
    public static IEnumerable<CodeInstruction> InsertBeforeSignature(
        this IEnumerable<CodeInstruction> instructions,
        IReadOnlyList<OpCode> signature,
        IReadOnlyList<CodeInstruction> newInstructions,
        bool all = false)
    {
        RequireSignature(signature);
        var found = false;
        var curInstIdx = 0;
        var allInstructions = instructions.ToArray();

        while (curInstIdx < allInstructions.Length)
        {
            if (found && !all)
            {
                // Return the rest of the instructions
                yield return allInstructions[curInstIdx++];
                continue;
            }

            var match = MatchesAt(allInstructions, curInstIdx, signature);

            if (!match)
            {
                // Emit current instruction and try matching from next instruction
                yield return allInstructions[curInstIdx++];
            }
            else
            {
                found = true;

                foreach (var newInstruction in newInstructions)
                    yield return newInstruction;

                yield return allInstructions[curInstIdx++];
            }
        }

        if (!found)
            throw new InvalidOperationException("Could not find the target signature in method!");
    }

    /// <summary>
    ///     Finds and no-ops an IL bytecode signature.
    /// </summary>
    /// <param name="instructions">The input instructions to patch, mainly coming from a HarmonyTranspiler.</param>
    /// <param name="signature">The signature to find within the instructions based on IL opcodes.</param>
    /// <param name="all">Match all instances of a signature.</param>
    public static IEnumerable<CodeInstruction> NoopSignature(
        this IEnumerable<CodeInstruction> instructions,
        IReadOnlyList<OpCode> signature,
        bool all = false)
    {
        RequireSignature(signature);
        var found = false;
        var curInstIdx = 0;
        var allInstructions = instructions.ToArray();

        while (curInstIdx < allInstructions.Length)
        {
            if (found && !all)
            {
                // Return the rest of the instructions
                yield return allInstructions[curInstIdx++];
                continue;
            }

            var match = MatchesAt(allInstructions, curInstIdx, signature);

            if (!match)
            {
                // Emit current instruction and try matching from next instruction
                yield return allInstructions[curInstIdx++];
            }
            else
            {
                found = true;

                for (var i = 0; i < signature.Count; i++)
                {
                    var curInst = allInstructions[curInstIdx++]!;
                    curInst.opcode = OpCodes.Nop;
                    curInst.operand = null;

                    yield return curInst;
                }
            }
        }

        if (!found)
            throw new InvalidOperationException("Could not find the target signature in method!");
    }

    /// <summary>
    ///     Finds and no-ops everything after a certain IL bytecode signature.
    /// </summary>
    /// <param name="instructions">The input instructions to patch, mainly coming from a HarmonyTranspiler.</param>
    /// <param name="signature">The signature to find within the instructions based on IL opcodes.</param>
    /// <param name="replaceAfterSignature">The amount of instructions to replace after the signature.</param>
    /// <param name="all">Match all instances of a signature.</param>
    public static IEnumerable<CodeInstruction> NoopAfterSignature(
        this IEnumerable<CodeInstruction> instructions,
        OpCode[] signature,
        uint replaceAfterSignature,
        bool all = false)
    {
        RequireSignature(signature);
        var allInstructions = instructions.ToArray();
        var found = false;
        for (var index = 0; index < allInstructions.Length; index++)
        {
            if ((!found || all) && MatchesAt(allInstructions, index, signature))
            {
                if (replaceAfterSignature > allInstructions.Length - index - signature.Length)
                    throw new InvalidOperationException("Not enough space in method to noop more instructions!");
                found = true;
                for (var offset = 0; offset < signature.Length; offset++)
                    yield return allInstructions[index++];
                for (var offset = 0u; offset < replaceAfterSignature; offset++)
                {
                    var instruction = allInstructions[index++];
                    instruction.opcode = OpCodes.Nop;
                    instruction.operand = null;
                    yield return instruction;
                }
                index--;
            }
            else yield return allInstructions[index];
        }

        if (!found)
            throw new InvalidOperationException("Could not find the target signature in method!");
    }

    private static void RequireSignature(IReadOnlyList<OpCode> signature)
    {
        if (signature.Count == 0)
            throw new ArgumentException("An IL signature must not be empty.", nameof(signature));
    }

    private static bool MatchesAt(IReadOnlyList<CodeInstruction> instructions, int index,
        IReadOnlyList<OpCode> signature)
    {
        if (signature.Count > instructions.Count - index) return false;
        for (var offset = 0; offset < signature.Count; offset++)
            if (instructions[index + offset].opcode != signature[offset]) return false;
        return true;
    }

}
