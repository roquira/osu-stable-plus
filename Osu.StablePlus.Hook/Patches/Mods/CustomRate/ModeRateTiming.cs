using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.GameModes.Play.Rulesets;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Explicit capabilities: arbitrary clocks do not imply support for DA or axes.</summary>
internal static class ModeRateTiming
{
    private static readonly bool[] ready = { true, false, false, false };
    internal static bool Supports(int mode) => mode >= 0 && mode < ready.Length && ready[mode];
    internal static int EffectiveMode => RateControl.IsSongSelection
        ? StandardModLayout.ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null))
        : Player.CurrentScore.Get() is { } score ? StandardModLayout.ModeOf(score)
        : StandardModLayout.ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null));

    internal static double Resolve(int mods, double native)
    {
        var mode = EffectiveMode;
        // Standard's existing timing implementation is unchanged.
        if (mode == 0 || !Supports(mode) || !CustomRateOptions.IsEnabled(mods)) return native;
        if (!RateControl.IsSongSelection && Player.CurrentScore.Get() is { } score)
            return RateControl.ForScore(score).Speed;
        return CustomRateOptions.SelectedFor(mods).Speed;
    }
    internal static double Multiply(double time, int mods) => time * Resolve(mods, CustomRateOptions.IsEnabled(mods) ? RateSettings.DefaultFor(mods) : 1);
    internal static double Divide(double time, int mods) => time / Resolve(mods, CustomRateOptions.IsEnabled(mods) ? RateSettings.DefaultFor(mods) : 1);
    internal static double MultiplyCurrent(double time) => Multiply(time, ModManager.ModStatus.Get());
    internal static double DivideCurrent(double time) => Divide(time, ModManager.ModStatus.Get());
    internal static double Constant(double native) => Resolve(
        !RateControl.IsSongSelection && Player.CurrentScore.Get() is { } score
            ? RateControl.GetMods(score) : ModManager.ModStatus.Get(), native);
    internal static float Single(float native) => (float)Constant(native);

    internal static readonly MethodInfo[] Helpers = DifficultyControl.Timing.DeclaringType!
        .GetMethods(DifficultyControl.All).Where(m => m.IsStatic && m.ReturnType == typeof(double) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(double), Osu.StablePlus.Stubs.Root.Mods.Type.Reference }) &&
            HasRates(m)).ToArray();
    internal static readonly MethodInfo[] Wrappers = DifficultyControl.Timing.DeclaringType!
        .GetMethods(DifficultyControl.All).Where(m => m.IsStatic && m.ReturnType == typeof(double) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(double) }) &&
            MethodReader.GetInstructions(m).Any(i => i.Operand is MethodInfo call && Helpers.Contains(call))).ToArray();
    internal static bool HasRates(MethodBase m)
    {
        var il = MethodReader.GetInstructions(m).ToArray();
        return il.Any(i => (i.Opcode == Ldc_R8 || i.Opcode == Ldc_R4) && Convert.ToDouble(i.Operand) == 1.5) &&
               il.Any(i => (i.Opcode == Ldc_R8 || i.Opcode == Ldc_R4) && Convert.ToDouble(i.Operand) == .75);
    }
    internal static MethodInfo ManiaWindow => DifficultyControl.Timing.DeclaringType!.Assembly.GetTypes()
        .Where(t => t != DifficultyControl.Timing.DeclaringType && DifficultyControl.Timing.DeclaringType.IsAssignableFrom(t))
        .SelectMany(t => t.GetMethods(DifficultyControl.All)).Single(m => m.ReturnType == typeof(int) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(double) }) && HasRates(m));
    // Mania has a separate scroll-speed setter. Its rate-adjusted reference
    // BPM normalises fixed scrolling, independently of judgement windows.
    internal static MethodInfo ManiaScroll => DifficultyControl.Timing.DeclaringType!.Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(DifficultyControl.All)).Single(m => m.IsStatic && m.ReturnType == typeof(bool) &&
            m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(double) &&
            m.GetParameters()[1].ParameterType == typeof(bool) && m.GetParameters()[2].ParameterType.IsEnum && HasRates(m));
    internal static MethodInfo CatchInitialize => Ruleset.Class.Reference.Assembly.GetTypes()
        .Where(t => t != Ruleset.Class.Reference && Ruleset.Class.Reference.IsAssignableFrom(t))
        .SelectMany(t => t.GetMethods(DifficultyControl.All)).Single(m => m.IsVirtual && m.ReturnType == typeof(void) &&
            m.GetParameters().Length == 0 && HasRates(m));

    internal static void VerifyInstallation()
    {
        try
        {
            bool Patched(MethodBase m, Type patch) => Harmony.GetPatchInfo(m)?.Transpilers.Any(p => p.PatchMethod.DeclaringType == patch) == true;
            var common = Helpers.Length == 2 && Wrappers.Length == 2 && ModeRateCalls.Targets().Any() &&
                ModeRateCalls.Targets().All(m => Patched(m, typeof(ModeRateCalls))) &&
                CustomRatePlayback.Targets().All(m => Patched(m, typeof(CustomRatePlayback))) &&
                Patched(CustomRateTiming.Target(), typeof(CustomRateTiming));
            ready[1] = common;
            ready[2] = common && Patched(CatchInitialize, typeof(ModeRateConstants));
            ready[3] = common && Patched(ManiaWindow, typeof(ModeRateConstants)) &&
                Patched(ManiaScroll, typeof(ModeRateConstants));
        }
        catch (Exception e) { Console.WriteLine("[Mode rates] Binding validation failed: " + e); }
        for (var i = 1; i <= 3; i++)
            if (!ready[i]) Console.WriteLine($"[Mode rates] Mode {i}: required timing hooks unavailable; custom rates disabled, native rates retained.");
    }
}

[OsuPatch, HarmonyPatch]
internal static class ModeRateCalls
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets()
    {
        if (ModeRateTiming.Helpers.Length != 2) throw new InvalidOperationException("Expected native clock conversion pair.");
        return DifficultyControl.Timing.DeclaringType!.Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(DifficultyControl.All)).Where(m => m.GetMethodBody() != null &&
                m != SelectionDetails.Tooltip && m != SelectionDetails.Update &&
                // Detouring x87 double-return methods is unsafe in stable's CLR. Rewrite
                // their consumers instead; unmanaged return conventions stay native.
                m.ReturnType != typeof(double) && MethodReader.GetInstructions(m)
                    .Any(i => i.Operand is MethodInfo call && (ModeRateTiming.Helpers.Contains(call) || ModeRateTiming.Wrappers.Contains(call))));
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var i in instructions)
        {
            if (i.operand is MethodInfo call && (ModeRateTiming.Helpers.Contains(call) || ModeRateTiming.Wrappers.Contains(call)))
            {
                bool wrapper = ModeRateTiming.Wrappers.Contains(call);
                var operation = wrapper ? MethodReader.GetInstructions(call).Select(op => op.Operand).OfType<MethodInfo>().Single(ModeRateTiming.Helpers.Contains) : call;
                string method = MethodReader.GetInstructions(operation).Any(op => op.Opcode == Div)
                    ? (wrapper ? nameof(ModeRateTiming.DivideCurrent) : nameof(ModeRateTiming.Divide))
                    : (wrapper ? nameof(ModeRateTiming.MultiplyCurrent) : nameof(ModeRateTiming.Multiply));
                i.operand = AccessTools.Method(typeof(ModeRateTiming), method);
            }
            yield return i;
        }
    }
}

[OsuPatch, HarmonyPatch]
internal static class ModeRateConstants
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets()
    {
        yield return ModeRateTiming.ManiaWindow;
        yield return ModeRateTiming.ManiaScroll;
        var initialize = ModeRateTiming.CatchInitialize.GetBaseDefinition();
        foreach (var type in Ruleset.Class.Reference.Assembly.GetTypes().Where(Ruleset.Class.Reference.IsAssignableFrom))
            foreach (var method in type.GetMethods(DifficultyControl.All))
                if (method.IsVirtual && method.GetBaseDefinition() == initialize && MethodReader.GetInstructions(method)
                    .Any(i => (i.Opcode == Ldc_R8 || i.Opcode == Ldc_R4) && (Convert.ToDouble(i.Operand) == 1.5 || Convert.ToDouble(i.Operand) == .75)))
                    yield return method;
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var i in instructions)
        {
            yield return i;
            if ((i.opcode == Ldc_R8 || i.opcode == Ldc_R4) &&
                (Convert.ToDouble(i.operand) == 1.5 || Convert.ToDouble(i.operand) == .75))
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(ModeRateTiming),
                    i.opcode == Ldc_R4 ? nameof(ModeRateTiming.Single) : nameof(ModeRateTiming.Constant)));
        }
    }
}
