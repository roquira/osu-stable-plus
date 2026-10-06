using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.Audio;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.AudioPreview;

/// <summary>Give preview streams a BASS_FX tempo stream so DT can preserve pitch.</summary>
[OsuPatch, HarmonyPatch]
internal static class FixUpdatePlaybackRate
{
    [HarmonyTargetMethod]
    private static MethodBase Target() => AudioTrackBass.Constructor.Reference;

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Both Preview checks select the fast path: stream flags, then skipping TempoCreate.
        // Match the called getter rather than local-variable numbers, which changed in stable.
        var preview = MethodReader.GetInstructions(AudioTrackBass.Constructor.Reference)
            .Select(i => i.Operand).OfType<MethodInfo>()
            .Where(m => m.DeclaringType == AudioTrackBass.Class.Reference.BaseType &&
                        !m.IsStatic && m.ReturnType == typeof(bool) && m.GetParameters().Length == 0)
            .Distinct().Single();
        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(preview))
            {
                // Consume this; leave false on the stack. Keep all branch labels in place.
                instruction.opcode = Pop;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(Ldc_I4_0);
                replaced++;
            }
            else yield return instruction;
        }
        if (replaced != 2) throw new InvalidOperationException("Expected two audio preview initialization checks.");
    }
}
