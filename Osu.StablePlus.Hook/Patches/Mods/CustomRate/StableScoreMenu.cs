using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[OsuPatch, HarmonyPatch]
internal static class StableScoreMenu
{
    private sealed class State { internal string? Key; }
    private static readonly ConditionalWeakTable<object, State> States = new();
    internal static readonly MethodInfo Update = NativeModMenu.Menu.GetDeclaredMethods().Single(m =>
        m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
        MethodReader.GetInstructions(m).Any(i => i.Operand is MethodInfo call && IsMultiplier(call)));
    private static bool IsMultiplier(MethodInfo m) => m.DeclaringType == ModManager.Class.Reference &&
        m.ReturnType == typeof(double) && m.GetParameters().Length == 0;
    [HarmonyTargetMethod] private static MethodBase Target() => Update;

    internal static void Refresh(object menu)
    {
        var mods = ModManager.ModStatus.Get();
        var key = mods + "/" + CustomRateOptions.SelectedFor(mods).Speed + "/" + StableScoringOptions.Enabled.Value + "/" + NativeStableScoring.Ready + "/" + ModeRateTiming.EffectiveMode;
        var state = States.GetValue(menu, _ => new State());
        if (state.Key == key) return;
        state.Key = key;
        Update.Invoke(menu, null);
    }

    private static bool Enabled => NativeStableScoring.Supports(StandardModLayout.ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null))) && StableScoringOptions.Enabled.Value;
    internal static double Multiplier(double native)
    {
        var mods = ModManager.ModStatus.Get();
        if (!Enabled || (mods & StableScoreMath.ScoreV2) != 0) return native;
        var mode = StandardModLayout.ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null));
        // Stable's zero RX/AP multiplier describes its assisted scoring rules.
        // The separate per-award rate adjustment is not an overall score multiplier.
        if (mode == 0 && (mods & StableScoreMath.Assistance) != 0) return native;
        var ratio = StableScoreMath.RelativeRate(mods, CustomRateOptions.SelectedFor(mods).Speed, mode);
        return ratio == 1 ? native : native * ratio;
    }
    internal static string Label(string native)
    {
        if (!Enabled) return native;
        var mods = ModManager.ModStatus.Get();
        if ((mods & StableScoreMath.ScoreV2) != 0) return native + " (custom scoring inactive)";
        return native;
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is not MethodInfo call) continue;
            if (IsMultiplier(call)) yield return new CodeInstruction(Call, AccessTools.Method(typeof(StableScoreMenu), nameof(Multiplier)));
            if (call.DeclaringType == typeof(string) && call.Name == "Format")
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(StableScoreMenu), nameof(Label)));
        }
    }
}
