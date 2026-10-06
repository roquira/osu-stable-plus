using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Annotate only the song selection's large current-mod label.</summary>
[OsuPatch, HarmonyPatch]
internal static class SelectedRateDisplay
{
    internal static readonly MethodInfo LongFormatter = ModManager.Class.Reference
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Single(m =>
            m.ReturnType == typeof(string) && m.GetParameters().Length == 2 &&
            m.GetParameters()[0].ParameterType == Osu.StablePlus.Stubs.Root.Mods.Type.Reference &&
            m.GetParameters()[1].ParameterType == typeof(bool));

    [HarmonyTargetMethod]
    internal static MethodBase Target() => SongSelection.ChoseBestSortMode.Reference.DeclaringType!
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        .Single(m => m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, LongFormatter)));

    [HarmonyPostfix]
    private static void UpdatePanels(object __instance)
    {
        foreach (var panel in ModMenuRateControl.Active) panel.Refresh();
        SelectionDetails.Tick(__instance);
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(LongFormatter))
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(SelectedRateDisplay), nameof(FormatCurrent)));
        }
    }

    private static string FormatCurrent(string text)
    {
        var mods = ModManager.ModStatus.Get();
        CustomRateOptions.ObserveMods(mods);
        return ModLabelFormatter.Format(text, mods, CustomRateOptions.ForMods(mods),
            DifficultyControl.CurrentBeatmap.Invoke(null, null), true, false, false);
    }

    internal static string Format(string text, int mods, double speed)
    {
        return ModLabelFormatter.Format(text, mods, new RateSettings(speed, false), null, true, false, false);
    }

    internal static string RateName(int mods)
    {
        var rateMod = (mods & Nightcore) != 0 ? Nightcore | DoubleTime : (mods & HalfTime) != 0 ? HalfTime : DoubleTime;
        return (string)LongFormatter.Invoke(null, [Enum.ToObject(Osu.StablePlus.Stubs.Root.Mods.Type.Reference, rateMod), false]);
    }
}
