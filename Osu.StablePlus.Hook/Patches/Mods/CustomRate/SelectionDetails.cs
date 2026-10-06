using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Hook.Patches.LivePerformance;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Only the selected map's information panel consumes these values.</summary>
internal static class SelectionDetails
{
    internal static readonly MethodInfo Update = SongSelection.ChoseBestSortMode.Reference.DeclaringType!
        .GetMethods(DifficultyControl.All).Single(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Ldc_R4 && Equals(i.Operand, 1.5f)) &&
            MethodReader.GetInstructions(m).Any(i => i.Operand is MethodInfo call && call.Name == "op_Multiply" &&
                call.ReturnType.FullName == "Microsoft.Xna.Framework.Vector3"));
    private static readonly MethodInfo[] Calls = MethodReader.GetInstructions(Update).Select(i => i.Operand).OfType<MethodInfo>().ToArray();
    internal static readonly MethodInfo Description = Calls.Single(m => m.DeclaringType == Beatmap.Class.Reference &&
        m.ReturnType == typeof(string) && MethodReader.GetInstructions(m).Count(i => i.Opcode == Ldfld &&
            i.Operand is FieldInfo f && DifficultyControl.Fields.Contains(f)) == 4);
    internal static readonly MethodInfo Tooltip = Calls.Single(m => m.DeclaringType == Beatmap.Class.Reference &&
        m.ReturnType == typeof(string) && MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, DifficultyControl.Timing)));
    internal static readonly MethodInfo TimeConversion = Calls.First(m => m.IsStatic && m.ReturnType == typeof(double) &&
        m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(double) }));
    private static readonly Regex Values = new(@"\b(CS|AR|OD|HP):[-+]?\d+(?:[.,]\d+)?(?:[▲▼▴▾△▽^]+)?");
    private static readonly Regex Stars = new(@"(?<label>Star Rating:\s*)[-+]?\d+(?:[.,]\d+)?(?<star>★?)");
    private static readonly ConditionalWeakTable<object, RefreshState> States = new();
    [ThreadStatic] internal static double? TooltipRate;
    [ThreadStatic] internal static object? TooltipMap;
    [ThreadStatic] internal static DifficultySettings? TooltipDifficulty;

    internal static double ClockRate => CustomRateOptions.IsEnabled(ModManager.ModStatus.Get())
        ? CustomRateOptions.ForMods(ModManager.ModStatus.Get()).Speed : 1;
    internal static float Rate(float original) => (float)ClockRate;
    internal static double Time(double original) => original / ClockRate;

    internal static double[] Configured(object map, int mods, DifficultySettings? da) => Enumerable.Range(0, 4)
        .Select(i => ApplyDifficultyMods(da?.Values[i] ?? DifficultyControl.Default(map, i), i, mods)).ToArray();

    internal static double ApplyDifficultyMods(double value, int index, int mods)
    {
        // Match stable gameplay: DA replaces the field, then native EZ/HR applies.
        if ((mods & Easy) != 0) value = Math.Max(0, value / 2);
        if ((mods & HardRock) != 0) value = Math.Min(10, value * (index == 1 ? 1.3 : 1.4));
        return value;
    }

    internal static string Format(string original, double[] configured, double rate, string stars, string maxPp)
    {
        var text = Values.Replace(original, match =>
        {
            var index = Array.IndexOf(new[] { "HP", "CS", "AR", "OD" }, match.Groups[1].Value);
            return match.Groups[1].Value + ":" + DifficultySettings.Effective(index, configured[index], rate)
                .ToString("0.##", CultureInfo.InvariantCulture);
        });
        return Stars.Replace(text, match => match.Groups["label"].Value + stars + match.Groups["star"].Value + " Max PP:" + maxPp + "pp");
    }

    internal static string Describe(object map)
    {
        var original = (string)Description.Invoke(map, null);
        var mods = ModManager.ModStatus.Get();
        var mode = StandardModLayout.ModeFor(map);
        var da = mode == 0 && (mods & DifficultyControl.Flag) != 0 ? DifficultyControl.Selected : null;
        var calculation = SelectionStars.GetValues(map, mods, ClockRate, da);
        if (mode != 0) return FormatOther(original, mode, ClockRate, calculation.Stars, calculation.MaxPp);
        return Format(original, Configured(map, mods, da), ClockRate, calculation.Stars, calculation.MaxPp);
    }

    internal static string FormatOther(string original, int mode, double rate, string stars, string maxPp)
    {
        // Keep each mode's native CS/key-count, HP and difficulty-mod rules.
        // Only catch AR and taiko OD have the corresponding clock transforms.
        var text = Values.Replace(original, match =>
        {
            string name = match.Groups[1].Value;
            var number = Regex.Match(match.Value, @"[-+]?\d+(?:[.,]\d+)?").Value.Replace(',', '.');
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return match.Value;
            if (mode == 2 && name == "AR") value = DifficultySettings.Effective(2, value, rate);
            else if (mode == 1 && name == "OD") value = (50 - (50 - 3 * value) / rate) / 3;
            // Mania's real-time windows are rate-compensated. Catch has no OD
            // judgement window; don't apply standard's OD equation to either.
            return name + ":" + value.ToString("0.##", CultureInfo.InvariantCulture);
        });
        return Stars.Replace(text, match => match.Groups["label"].Value + stars + match.Groups["star"].Value + " Max PP:" + maxPp + "pp");
    }

    internal static string DescribeTooltip(object map)
    {
        try
        {
            TooltipMap = map;
            TooltipRate = ClockRate;
            TooltipDifficulty = DifficultyControl.Enabled ? DifficultyControl.Selected : null;
            return (string)Tooltip.Invoke(map, null);
        }
        finally { TooltipMap = null; TooltipRate = null; TooltipDifficulty = null; }
    }

    internal static void Tick(object selection)
    {
        if (!RateControl.IsSongSelection) return;
        StandardModLayout.ObserveSelection();
        var map = DifficultyControl.CurrentBeatmap.Invoke(null, null);
        if (map == null) return;
        var state = States.GetOrCreateValue(selection);
        var mods = ModManager.ModStatus.Get();
        var key = mods + "/" + StarRatingOptions.CalculationMods(mods) + "/" + ClockRate.ToString("R", CultureInfo.InvariantCulture) + "/" +
            string.Join("/", DifficultyControl.Selected.Values.Select(v => v?.ToString("R", CultureInfo.InvariantCulture))) +
            "/" + MirrorSettings.Selected + "/" + StandardModLayout.ModeFor(map) + "/" + SelectionStars.Revision + "/" + PerformanceOptions.Precision + "/" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 5;
        if (ReferenceEquals(state.Map, map) && state.Key == key) return;
        state.Map = map;
        state.Key = key;
        Update.Invoke(selection, null);
    }

    private sealed class RefreshState { public object? Map; public string? Key; }
}

[OsuPatch, HarmonyPatch]
internal static class SelectedBeatmapDetails
{
    [HarmonyTargetMethod] internal static MethodBase Target() => SelectionDetails.Update;
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            var replacement = instruction.Calls(SelectionDetails.Description) ? nameof(SelectionDetails.Describe) :
                instruction.Calls(SelectionDetails.Tooltip) ? nameof(SelectionDetails.DescribeTooltip) :
                instruction.Calls(SelectionDetails.TimeConversion) ? nameof(SelectionDetails.Time) : null;
            if (replacement != null)
                yield return new CodeInstruction(instruction) { opcode = Call, operand = AccessTools.Method(typeof(SelectionDetails), replacement) };
            else
            {
                yield return instruction;
                if (instruction.opcode == Ldc_R4 && (Equals(instruction.operand, 1.5f) || Equals(instruction.operand, 0.75f)))
                    yield return new CodeInstruction(Call, AccessTools.Method(typeof(SelectionDetails), nameof(SelectionDetails.Rate)));
            }
        }
    }
}

[OsuPatch, HarmonyPatch]
internal static class SelectedBeatmapTooltipRate
{
    [HarmonyTargetMethod] internal static MethodBase Target() => SelectionDetails.Tooltip;
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Keep the native double-return method's ABI untouched; replace its callers.
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(SelectionDetails.TimeConversion))
                yield return new CodeInstruction(instruction)
                {
                    opcode = Call,
                    operand = AccessTools.Method(typeof(SelectedBeatmapTooltipRate), nameof(ConvertTime))
                };
            else yield return instruction;
        }
    }
    internal static double ConvertTime(double value) => SelectionDetails.TooltipRate.HasValue ? value / SelectionDetails.TooltipRate.Value :
        (double)SelectionDetails.TimeConversion.Invoke(null, [value]);
}
