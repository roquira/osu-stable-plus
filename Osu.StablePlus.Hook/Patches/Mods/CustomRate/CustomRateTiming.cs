using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Keep the audio/wall-clock drift calculation consistent with custom DT.</summary>
[OsuPatch, HarmonyPatch]
internal static class CustomRateTiming
{
    [HarmonyPostfix]
    private static void After() => LivePerformance.TrackOtherModeProgress.Capture();
    [HarmonyTargetMethod]
    internal static MethodBase Target() => Player.Class.Reference.GetDeclaredMethods().Single(method =>
    {
        var il = MethodReader.GetInstructions(method).ToArray();
        return il.Any(i => i.Opcode == Ldc_R4 && Equals(i.Operand, 1.5f)) &&
               il.Any(i => i.Opcode == Ldc_R4 && Equals(i.Operand, 0.75f)) &&
               il.Any(i => i.Opcode == Ldc_R4 && Equals(i.Operand, 60f)) &&
               il.Any(i => Equals(i.Operand, AccessTools.Method(typeof(Math), nameof(Math.Abs), [typeof(float)])));
    });

    internal static float ExpectedDtRate() => (float)RateControl.GameplayMultiplier();

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            // Replace each rate mod's expected clock. Keep the drift threshold,
            // counters, warning, invalidation and unrelated wall-clock check intact.
            if (instruction.opcode == Ldc_R4 && (Equals(instruction.operand, 1.5f) || Equals(instruction.operand, 0.75f)))
            {
                instruction.opcode = Call;
                instruction.operand = AccessTools.Method(typeof(CustomRateTiming), nameof(ExpectedDtRate));
                count++;
            }
            yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException("Expected DT and HT rates in the audio timing check.");
    }
}
