using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.GameModes.Play;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.UI;

/// <summary>
///     Makes the mods list overlay that is shown when entering play mode always show, like in a replay
///     but faded to a user customizable amount.
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
internal static class ShowModsInGameplay
{
    private static readonly MethodInfo ReplayMode = Osu.StablePlus.Utils.IL.MethodReader.GetInstructions(Player.OnLoadComplete.Reference)
        .SkipWhile(i => !(i.Opcode == Ldc_R4 && Equals(i.Operand, 94f)))
        .Where(i => i.Opcode == Call).Select(i => i.Operand).OfType<MethodInfo>().First();
    internal static bool AlwaysDraw() => (bool)ReplayMode.Invoke(null, null) || GameplayModIconOptions.Percent > 0;
    internal static float FinalOpacity() => GameplayModIconOptions.Percent / 100f;

    [UsedImplicitly]
    [HarmonyTargetMethod]
    private static MethodBase Target() => Player.OnLoadComplete.Reference;

    [HarmonyPrefix]
    private static bool Before(bool __0, ref bool __result)
    {
        if (!__0 || Mods.CustomRate.RulesetCompatibility.AllowLoadedReplay()) return true;
        __result = true;
        return false;
    }

    [UsedImplicitly]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Replace a "0f" representing a fade target value after an arbitrary "94f" constant
        // Also set the pSprite parameter "alwaysDraw" to true

        var replaceState = ReplaceState.Find;

        return Mods.CustomRate.DifficultyModHud.Transpiler(instructions.Manipulator(
            inst =>
            {
                switch (replaceState)
                {
                    case ReplaceState.Find when inst.Is(Ldc_R4, 94f):
                        replaceState = ReplaceState.ReplaceAlwaysDraw;
                        return false;

                    // This is calling "InputManager.get_ReplayMode()"
                    case ReplaceState.ReplaceAlwaysDraw when inst.opcode == Call:
                        return true;

                    // This is loading the transformation fade end value
                    case ReplaceState.ReplaceFadeEndValue when inst.Is(Ldc_R4, 0f):
                        return true;

                    case ReplaceState.Finished:
                    default:
                        return false;
                }
            },
            inst =>
            {
                switch (replaceState)
                {
                    case ReplaceState.ReplaceAlwaysDraw:
                        inst.opcode = Call;
                        inst.operand = AccessTools.Method(typeof(ShowModsInGameplay), nameof(AlwaysDraw));
                        replaceState = ReplaceState.ReplaceFadeEndValue;
                        break;
                    case ReplaceState.ReplaceFadeEndValue:
                        inst.opcode = Call;
                        inst.operand = AccessTools.Method(typeof(ShowModsInGameplay), nameof(FinalOpacity));
                        replaceState = ReplaceState.Finished;
                        break;
                    case ReplaceState.Find:
                    case ReplaceState.Finished:
                    default:
                        throw new Exception();
                }
            }
        ));
    }

    private enum ReplaceState
    {
        Find,
        ReplaceAlwaysDraw,
        ReplaceFadeEndValue,
        Finished,
    }
}
