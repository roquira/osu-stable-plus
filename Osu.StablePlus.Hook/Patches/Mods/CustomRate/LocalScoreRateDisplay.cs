using Osu.StablePlus.Hook.Patches.LivePerformance;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.XNA;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Format the score's own rate in its leaderboard row and hover details.</summary>
[OsuPatch, HarmonyPatch]
internal static class LocalScoreRateDisplay
{
    internal const float ScoreModLabelWidth = 160f;

    [HarmonyTargetMethod]
    internal static MethodBase Target() => SongSelection.ChoseBestSortMode.Reference.DeclaringType!
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m =>
            m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == Score.Class.Reference &&
            MethodReader.GetInstructions(m).Any(i => IsModsFormatter(i.Operand)));

    private static bool IsModsFormatter(object? operand) => operand is MethodInfo m &&
        m.DeclaringType == ModManager.Class.Reference && m.ReturnType == typeof(string);

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var source = instructions.ToList();
        var count = 0;
        var scoreLines = 0;
        var scoreWidths = 0;
        var scoreCombo = false;
        var shortLabel = false;
        for (var index = 0; index < source.Count; index++)
        {
            var instruction = source[index];
            if (shortLabel && instruction.opcode == Ldc_R4 && Equals(instruction.operand, 100f) &&
                index + 2 < source.Count && source[index + 1].opcode == Ldc_R4 && Equals(source[index + 1].operand, 0f) &&
                source[index + 2].opcode == Newobj && Equals(source[index + 2].operand, Vector2.Constructor.Reference))
            {
                instruction = new CodeInstruction(instruction) { operand = ScoreModLabelWidth };
                scoreWidths++;
            }
            if (instruction.opcode == Stfld && Equals(instruction.operand, NativeModMenu.ModTooltip))
            {
                var load = new CodeInstruction(Ldarg_1);
                load.labels.AddRange(instruction.labels); load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(LocalScorePerformance), nameof(LocalScorePerformance.AssignTooltip)));
                continue;
            }
            yield return instruction;
            if (instruction.opcode == Ldfld && Equals(instruction.operand, Score.MaxCombo.Reference)) scoreCombo = true;
            if (scoreCombo && instruction.operand is MethodInfo format && format.DeclaringType == typeof(string) && format.Name == nameof(string.Format))
            {
                yield return new CodeInstruction(Ldarg_1);
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(LocalScorePerformance), nameof(LocalScorePerformance.ScoreLine)));
                scoreCombo = false;
                scoreLines++;
            }
            if (instruction.opcode == Newobj && instruction.operand is ConstructorInfo ctor && ctor.DeclaringType == pText.SetText.Reference.DeclaringType)
            {
                yield return new CodeInstruction(Dup);
                yield return new CodeInstruction(Ldarg_1);
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(LocalScorePerformance), nameof(LocalScorePerformance.AttachText)));
                shortLabel = false;
            }
            if (!IsModsFormatter(instruction.operand)) continue;
            shortLabel = !Equals(instruction.operand, SelectedRateDisplay.LongFormatter);
            yield return new CodeInstruction(Ldarg_1);
            yield return new CodeInstruction(Equals(instruction.operand, SelectedRateDisplay.LongFormatter) ? Ldc_I4_1 : Ldc_I4_0);
            yield return new CodeInstruction(Call, AccessTools.Method(typeof(LocalScoreRateDisplay), nameof(FormatLabel)));
            count++;
        }
        if (count == 0) throw new InvalidOperationException("Could not locate score mod labels.");
        if (scoreLines != 1) throw new InvalidOperationException("Expected one native score/combo label.");
        if (scoreWidths != 1) throw new InvalidOperationException("Expected one native score mod label width.");
    }

    internal static string Format(string text, object score)
        => FormatLabel(text, score, false);

    internal static string FormatLabel(string text, object score, bool longNames)
    {
        var rate = RateControl.ForScore(score);
        var map = Score.Beatmap.Get(score) ?? DifficultyControl.CurrentBeatmap.Invoke(null, null);
        var label = ModLabelFormatter.Format(text, RateControl.GetMods(score), rate, map, longNames, longNames);
        if (rate.UnsupportedRulesetSettings)
            label = string.IsNullOrEmpty(label) || label == "None" ? "Unsupported lazer settings" : label + " [unsupported lazer settings]";
        label = longNames && rate.Scoring?.Adjusted == true ? label + "\nScoring: Adjusted stable v1" : label;
        return longNames ? LocalScorePerformance.Decorate(label, score, true) : label;
    }
}
