using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal static class DifficultyControl
{
    // The remaining high bit is private to patched stable; remove it from lazer headers.
    internal const int Flag = unchecked((int)0x80000000);
    internal static bool Enabled
    {
        get => StandardModLayout.IsStandard && (ModManager.ModStatus.Get() & Flag) != 0;
        set => ModManager.ModStatus.Set(value ? ModManager.ModStatus.Get() | Flag : ModManager.ModStatus.Get() & ~Flag);
    }
    internal static DifficultySettings Selected = new(null, null, null, null);
    internal static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    internal static readonly MethodInfo CurrentBeatmap = SongSelection.ChoseBestSortMode.Reference.DeclaringType!
        .GetMethods(All).Where(m => m.GetMethodBody() != null).SelectMany(MethodReader.GetInstructions)
        .Select(i => i.Operand).OfType<MethodInfo>().Distinct().Single(m => m.IsStatic &&
            m.ReturnType == Beatmap.Class.Reference && m.GetParameters().Length == 0);
    // A double operand can only come from ldc.r8.
    internal static readonly MethodInfo Timing = Beatmap.Class.Reference.Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(All)).Where(m => m.GetMethodBody() != null).Single(m =>
            MethodReader.References(m, 1800d) && MethodReader.References(m, 450d) && MethodReader.References(m, 80d));
    internal static readonly FieldInfo[] Fields = FindFields();
    private static readonly int GameplayMode = Convert.ToInt32(Enum.Parse(Osu.StablePlus.Stubs.Root.GameBase.Mode.Reference.FieldType, "Play"));

    private static FieldInfo[] FindFields()
    {
        var il = MethodReader.GetInstructions(Timing).ToArray();
        FieldInfo Before(double constant) => il.TakeWhile(i => !(i.Opcode == Ldc_R8 && Equals(i.Operand, constant)))
            .Last(i => i.Opcode == Ldfld).Operand as FieldInfo ?? throw new InvalidOperationException("Difficulty field not found.");
        var ar = Before(1800);
        var od = Before(80);
        var cs = il.Where(i => i.Opcode == Ldfld).Select(i => i.Operand).OfType<FieldInfo>()
            .Distinct().Single(f => f.DeclaringType == ar.DeclaringType && f.FieldType == typeof(float) && f != ar && f != od);
        var hp = ar.DeclaringType!.GetFields(All).Single(f => f.FieldType == typeof(float) && f != ar && f != od && f != cs);
        return [hp, cs, ar, od];
    }

    internal static double Default(object? map, int index) => map == null ? 5 : (float)Fields[index].GetValue(map);
    internal static double Value(object? map, int index) => Selected.Values[index] ?? Default(map, index);
    internal static void Set(int index, double? value)
    {
        var values = (double?[])Selected.Values.Clone();
        values[index] = value.HasValue ? DifficultySettings.Normalize(value.Value) : null;
        Selected = new DifficultySettings(values);
    }
    internal static void Disable() { Enabled = false; Selected = new DifficultySettings(null, null, null, null); }

    internal static float Read(object map, int index)
    {
        if (ReferenceEquals(map, SelectionStars.CalculatingMap)) return (float)Default(map, index);
        if (ReferenceEquals(map, SelectionDetails.TooltipMap))
            return (float)(SelectionDetails.TooltipDifficulty?.Values[index] ?? Default(map, index));
        // Only gameplay reads are replaced. The native beatmap and database are untouched.
        var settings = StandardModLayout.IsStandard && Osu.StablePlus.Stubs.Root.GameBase.Mode.Get() == GameplayMode && Player.CurrentScore.Get() is { } score
            ? RateControl.ForScore(score).Difficulty : null;
        return (float)(settings?.Values[index] ?? Default(map, index));
    }
}

[OsuPatch, HarmonyPatch]
internal static class ApplyDifficultyAdjust
{
    internal static IEnumerable<MethodBase> Readers()
    {
        var manager = DifficultyControl.Timing.DeclaringType!;
        // This health method already has the RX/AP sound patch. That patch
        // composes our reads into its transpiler to avoid duplicate finalizers.
        var comboBreak = Osu.StablePlus.Hook.Patches.Relax.AllowRelaxComboBreakSound.Target();
        foreach (var type in manager.Assembly.GetTypes())
            foreach (var method in type.GetMethods(DifficultyControl.All).Cast<MethodBase>())
            {
                if (method.GetMethodBody() == null || type == Beatmap.Class.Reference ||
                    !DifficultyControl.Fields.Any(field => MethodReader.References(method, field))) continue;
                var il = MethodReader.GetInstructions(method).ToArray();
                var reads = il.Where(i => i.Opcode == Ldfld).Select(i => i.Operand).OfType<FieldInfo>().ToArray();
                if (!reads.Any(DifficultyControl.Fields.Contains)) continue;
                // Hit object managers own AR/OD/CS; ruleset drain and health processors read HP.
                var health = reads.Contains(DifficultyControl.Fields[0]) &&
                    ((method is MethodInfo m && m.ReturnType == typeof(double) && m.GetParameters().Length == 0) ||
                     (method.GetParameters().Length == 2 && method.GetParameters().All(p => !p.ParameterType.IsPrimitive)));
                if ((manager.IsAssignableFrom(type) || health) && method != comboBreak) yield return method;
            }
    }

    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() => Readers()
        .Where(m => ((MethodInfo)m).ReturnType != typeof(double));
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            var index = instruction.opcode == Ldfld ? Array.IndexOf(DifficultyControl.Fields, instruction.operand) : -1;
            if (index < 0) { yield return instruction; continue; }
            var arg = new CodeInstruction(Ldc_I4, index);
            arg.labels.AddRange(instruction.labels);
            arg.blocks.AddRange(instruction.blocks);
            yield return arg;
            yield return new CodeInstruction(Call, AccessTools.Method(typeof(DifficultyControl), nameof(DifficultyControl.Read)));
        }
    }
}
