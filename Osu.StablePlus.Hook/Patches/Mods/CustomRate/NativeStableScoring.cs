using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Hook.Patches.Relax;
using Osu.StablePlus.Stubs.GameModes.Play.Rulesets;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.GameplayElements.Scoring.Processors;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal static class NativeStableScoring
{
    internal static bool Ready;
    private static bool maniaReady;
    internal static bool Supports(int mode) => Ready && ModeRateTiming.Supports(mode) && (mode != 3 || maniaReady);
    internal static MethodInfo Difficulty => Bindings.Difficulty;
    internal static MethodInfo Initialize => Bindings.Initialize;
    internal static MethodInfo ModMultiplier => Bindings.ModMultiplier;
    internal static MethodInfo ScoreGetter => Bindings.ScoreGetter;
    internal static FieldInfo ScoreField => Bindings.ScoreField;
    internal static MethodInfo ScoreSetter => Bindings.ScoreSetter;

    // A future native binding failure must not poison the Ready flag or fresh-play
    // capture. Helpers stay on legacy scoring when the feature cannot be installed.
    private static class Bindings
    {
        internal static readonly MethodInfo Difficulty = Beatmap.Class.Reference.GetDeclaredMethods().Single(m =>
            m.ReturnType == typeof(int) && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Ldc_R4 && Equals(i.Operand, 38f)) &&
            MethodReader.GetInstructions(m).Any(i => i.Operand is MethodInfo call && call.DeclaringType == typeof(Math) && call.Name == "Round"));
        internal static readonly MethodInfo Initialize = Ruleset.Class.Reference.GetDeclaredMethods().Single(m =>
            m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, Difficulty)));
        internal static readonly MethodInfo ModMultiplier = MethodReader.GetInstructions(Initialize)
            .Select(i => i.Operand).OfType<MethodInfo>().Single(m => m.DeclaringType == ModManager.Class.Reference &&
                m.ReturnType == typeof(double) && m.GetParameters().Length == 3);
        internal static readonly MethodInfo ScoreGetter = Score.Class.Reference.GetDeclaredMethods().Single(m =>
            m.ReturnType == typeof(int) && m.IsVirtual && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => i.Operand is MethodInfo call &&
                call.DeclaringType == ScoreProcessor.AddScoreChange.Reference.DeclaringType && call.ReturnType == typeof(int)));
        internal static readonly FieldInfo ScoreField = MethodReader.GetInstructions(ScoreGetter)
            .Where(i => i.Opcode == Ldfld).Select(i => i.Operand).OfType<FieldInfo>().Last();
        internal static readonly MethodInfo ScoreSetter = Score.Class.Reference.GetDeclaredMethods().Single(m =>
            m.ReturnType == typeof(void) && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(int) }) &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Stfld && Equals(i.Operand, ScoreField)));
    }
    private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");

    internal static int EditedDifficulty(object map, DifficultySettings? settings)
    {
        if (settings == null) return (int)Difficulty.Invoke(map, null);
        var copy = Clone.Invoke(map, null);
        foreach (var i in new[] { 0, 1, 3 })
            if (settings.Values[i] is { } value) DifficultyControl.Fields[i].SetValue(copy, (float)value);
        return (int)Difficulty.Invoke(copy, null);
    }

    internal static bool Applies(object score, RateSettings settings) => Supports(StandardModLayout.ModeOf(score)) && settings.Scoring?.Adjusted == true &&
        (StandardModLayout.ModeOf(score) == 0 || settings.Scoring.Version >= 2 && ModeRateTiming.Supports(StandardModLayout.ModeOf(score))) &&
        (RateControl.GetMods(score) & StableScoreMath.ScoreV2) == 0;

    internal static int ResolveDifficulty(int native, object ruleset)
    {
        var score = Ruleset.CurrentScore.Get(ruleset);
        if (score == null) return native;
        var settings = RateControl.ForScore(score);
        if (!Applies(score, settings) || StandardModLayout.ModeOf(score) != 0) return native;
        var saved = settings.Scoring!;
        if (saved.DifficultyFactor is { } factor) return factor;
        var map = Score.Beatmap.Get(score) ?? DifficultyControl.CurrentBeatmap.Invoke(null, null);
        if (map == null) throw new InvalidOperationException("Cannot snapshot scoring difficulty without a beatmap.");
        factor = EditedDifficulty(map, settings.Difficulty);
        RateControl.Remember(score, settings.WithScoring(new StableScoringSettings(saved.Version, factor)));
        return factor;
    }

    internal static double ScaleModMultiplier(double native, object ruleset)
    {
        var score = Ruleset.CurrentScore.Get(ruleset);
        if (score == null) return native;
        var settings = RateControl.ForScore(score);
        if (!Applies(score, settings)) return native;
        var ratio = StableScoreMath.RelativeRate(RateControl.GetMods(score), settings.Speed, StandardModLayout.ModeOf(score));
        return ratio == 1 ? native : native * ratio;
    }

    internal static int AdjustAward(int native, object ruleset)
    {
        var score = Ruleset.CurrentScore.Get(ruleset);
        if (score == null) return native;
        var settings = RateControl.ForScore(score);
        var mods = RateControl.GetMods(score);
        return StandardModLayout.ModeOf(score) == 0 && Applies(score, settings) && (mods & StableScoreMath.Assistance) != 0
            ? StableScoreMath.AssistedAward(native, mods, settings.Speed) : native;
    }

    // Keep a partially installed feature inactive. The native paths are still usable.
    internal static void VerifyInstallation()
    {
        try
        {
            Ready = Has(typeof(InitializeStableScoring), Initialize) && Has(typeof(AllowRelaxComboBreakSound), AllowRelaxComboBreakSound.Target()) &&
                Has(typeof(CaptureGameplayRate), Osu.StablePlus.Stubs.GameModes.Play.Player.ResetScore.Reference) &&
                Has(typeof(ReadReplayRate), Score.ReadReplay.Reference) && Has(typeof(WriteReplayRate), Score.WriteReplay.Reference);
            var modeTargets = InitializeOtherModeScoring.Targets().ToArray();
            maniaReady = modeTargets.Length == 1 && modeTargets.All(m => Has(typeof(InitializeOtherModeScoring), m));
            if (!maniaReady) Console.WriteLine("[Stable scoring] Mania multiplier hook unavailable; retaining native mania scoring.");
        }
        catch (Exception e) { Ready = false; Console.WriteLine("[Stable scoring] Binding failed: " + e.Message); }
        if (!Ready) Console.WriteLine("[Stable scoring] Required hooks unavailable; retaining legacy scoring.");
    }

    private static bool Has(Type patch, MethodBase method) => Harmony.GetPatchInfo(method)?.Transpilers
        .Concat(Harmony.GetPatchInfo(method)!.Prefixes).Concat(Harmony.GetPatchInfo(method)!.Postfixes)
        .Any(p => p.PatchMethod.DeclaringType == patch) == true;
}

[OsuPatch, HarmonyPatch]
internal static class InitializeStableScoring
{
    [HarmonyTargetMethod] internal static MethodBase Target() => NativeStableScoring.Initialize;

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var difficulty = 0;
        var multiplier = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            string? helper = null;
            if (instruction.Calls(NativeStableScoring.Difficulty)) { helper = nameof(NativeStableScoring.ResolveDifficulty); difficulty++; }
            if (instruction.Calls(NativeStableScoring.ModMultiplier)) { helper = nameof(NativeStableScoring.ScaleModMultiplier); multiplier++; }
            if (helper == null) continue;
            yield return new CodeInstruction(Ldarg_0);
            yield return new CodeInstruction(Call, AccessTools.Method(typeof(NativeStableScoring), helper));
        }
        if (difficulty != 1 || multiplier != 1) throw new InvalidOperationException("Expected one native ScoreV1 difficulty/multiplier initialization.");
    }
}

// Mania owns its normalized ScoreV1 multiplier and does not call the base
// initializer. Taiko does call the base, so it must not be scaled a second time.
[OsuPatch, HarmonyPatch]
internal static class InitializeOtherModeScoring
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() => Ruleset.Class.Reference.Assembly.GetTypes()
        .Where(t => t != Ruleset.Class.Reference && Ruleset.Class.Reference.IsAssignableFrom(t))
        .SelectMany(t => t.GetMethods(DifficultyControl.All)).Where(m => m.IsVirtual &&
            m.GetBaseDefinition() == NativeStableScoring.Initialize.GetBaseDefinition() &&
            MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, NativeStableScoring.ModMultiplier)));
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int calls = 0;
        foreach (var i in instructions)
        {
            yield return i;
            if (!i.Calls(NativeStableScoring.ModMultiplier)) continue;
            calls++;
            yield return new CodeInstruction(Ldarg_0);
            yield return new CodeInstruction(Call, AccessTools.Method(typeof(NativeStableScoring), nameof(NativeStableScoring.ScaleModMultiplier)));
        }
        if (calls != 1) throw new InvalidOperationException("Expected one mode-specific ScoreV1 multiplier.");
    }
}

internal static class AdjustAssistedScore
{
    [HarmonyTargetMethod] internal static MethodBase Target() => AllowRelaxComboBreakSound.Target();

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var count = 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == Add && i + 1 < list.Count && list[i + 1].Calls(NativeStableScoring.ScoreSetter))
            {
                var arg = new CodeInstruction(Ldarg_0);
                arg.labels.AddRange(list[i].labels);
                list[i].labels.Clear();
                yield return arg;
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(NativeStableScoring), nameof(NativeStableScoring.AdjustAward)));
                count++;
            }
            yield return list[i];
        }
        if (count != 1) throw new InvalidOperationException("Expected one native ScoreV1 point award.");
    }
}
