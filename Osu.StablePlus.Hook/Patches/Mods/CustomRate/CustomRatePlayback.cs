using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.Audio;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[OsuPatch, HarmonyPatch]
internal static class CustomRatePlayback
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets()
    {
        // Includes initial audio setup, seek/restart, toggling DT while watching a replay,
        // and the replay slow/normal/fast button. Match audio calls, never arbitrary 1.5 constants.
        return Player.Class.Reference.GetDeclaredMethods().Where(method =>
        {
            var il = MethodReader.GetInstructions(method).ToArray();
            return il.Any(i => Equals(i.Operand, AudioEngine.SetCurrentPlaybackRate.Reference)) &&
                   il.Any(i => i.Opcode == Ldc_R8 && (Equals(i.Operand, 150.0) || Equals(i.Operand, 1.5)));
        });
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var parameters = original.GetParameters();
        var modToggle = parameters.Length == 2 && parameters[0].ParameterType == Stubs.Root.Mods.Type.Reference &&
                        parameters[1].ParameterType == typeof(bool);
        var il = instructions.ToList();
        for (var i = 0; i < il.Count; i++)
        {
            var instruction = il[i];
            if (instruction.opcode == Ldc_R8 && (Equals(instruction.operand, 150.0) || Equals(instruction.operand, 75.0)) &&
                i + 1 < il.Count && il[i + 1].Calls(AudioEngine.SetCurrentPlaybackRate.Reference))
            {
                instruction.opcode = Call;
                instruction.operand = AccessTools.Method(typeof(RateControl), nameof(RateControl.GameplayPercent));
            }
            else if (modToggle && instruction.opcode == Ldc_R8 && (Equals(instruction.operand, 1.5) || Equals(instruction.operand, 0.75)))
            {
                instruction.opcode = Call;
                instruction.operand = AccessTools.Method(typeof(RateControl), nameof(RateControl.GameplayMultiplier));
            }
            else if (instruction.Calls(AudioEngine.SetCurrentPlaybackRate.Reference))
                instruction.operand = AccessTools.Method(typeof(RateControl), nameof(RateControl.SetGameplayRate));
            yield return instruction;
        }
    }
}
