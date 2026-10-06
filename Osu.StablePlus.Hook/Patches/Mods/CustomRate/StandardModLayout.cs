using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal static class StandardModLayout
{
    internal static readonly MethodInfo PlayMode = MethodReader.GetInstructions(
        ModSelection.UpdateMods.Reference.DeclaringType!.GetConstructors(NativeModMenu.Members).Single())
        .Select(i => i.Operand).OfType<MethodInfo>().First(m => m.IsStatic &&
            m.ReturnType.FullName == "osu_common.PlayModes" && m.GetParameters().Length == 0);
    // A native taiko/catch/mania map keeps its own ruleset even when the mode
    // selector says osu!. Standard maps instead use the selected conversion mode.
    internal static readonly MethodInfo BeatmapMode = Beatmap.Class.Reference.GetMethods(DifficultyControl.All)
        .Single(m => !m.IsStatic && m.ReturnType == PlayMode.ReturnType && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, PlayMode)));
    internal static readonly MethodInfo NativeBeatmapMode = MethodReader.GetInstructions(BeatmapMode)
        .Select(i => i.Operand).OfType<MethodInfo>().First(m => !m.IsStatic && m.ReturnType == PlayMode.ReturnType);
    internal static int NativeModeOf(object map) => Convert.ToInt32(NativeBeatmapMode.Invoke(map, null));
    internal static int ModeFor(object? map) => Convert.ToInt32(map == null
        ? PlayMode.Invoke(null, null) : BeatmapMode.Invoke(map, null));
    internal static bool IsStandardMap(object? map) => ModeFor(map) == 0;
    internal static bool IsStandard => IsStandardMap(DifficultyControl.CurrentBeatmap.Invoke(null, null));
    internal static readonly FieldInfo ScoreMode = Score.Class.Reference.GetFields(DifficultyControl.All)
        .Single(f => f.FieldType == PlayMode.ReturnType);
    internal static int ModeOf(object score) => Convert.ToInt32(ScoreMode.GetValue(score));
    private static int? lastSelectionMode;
    internal static void ObserveSelection()
    {
        var mode = ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null));
        if (lastSelectionMode == mode) return;
        var previous = lastSelectionMode ?? Convert.ToInt32(PlayMode.Invoke(null, null));
        lastSelectionMode = mode;
        // The mode selector does not change when merely selecting a native map.
        // Clear custom flags on that transition too, without erasing mania MR
        // while browsing between mania difficulties.
        if (mode != 0 || previous != 0)
        {
            var clear = DifficultyControl.Flag | (previous != mode ? MirrorSettings.Flag : 0);
            ModManager.ModStatus.Set(ModManager.ModStatus.Get() & ~clear);
            DifficultyControl.Selected = new DifficultySettings(null, null, null, null);
            MirrorSettings.Selected = MirrorAxes.Horizontal;
        }
    }
    internal static float RowOffset(int row) => -10 - row * 10;

    internal static void ArrangeLabels(object menu)
    {
        var rowField = MethodReader.GetInstructions(NativeModMenu.ModPosition).Select(i => i.Operand)
            .OfType<FieldInfo>().Single();
        var originalRow = (float)rowField.GetValue(menu);
        var multiplier = NativeModMenu.Menu.GetFields(NativeModMenu.Members)
            .Single(f => f.FieldType == pText.SetText.Reference.DeclaringType).GetValue(menu);
        foreach (var sprite in NativeModDrawer.Sprites(NativeModMenu.Manager.GetValue(menu)))
        {
            if (sprite.GetType() != pText.SetText.Reference.DeclaringType) continue;
            var position = NativeModDrawer.Position.GetValue(sprite);
            var y = NativeModDrawer.GetY(position);
            if (ReferenceEquals(sprite, multiplier)) continue;
            else
            {
                var row = Enumerable.Range(0, 3).Where(r => Math.Abs(y - (originalRow + r * 60 - 13)) < 0.1).DefaultIfEmpty(-1).First();
                if (row < 0) continue;
                y += RowOffset(row);
            }
            NativeModDrawer.Place(sprite, NativeModDrawer.GetX(position), y);
        }
        var label = NativeModMenu.AddText(NativeModMenu.Manager.GetValue(menu), "Adjustments", 24, 20,
            originalRow + 180 + RowOffset(3) - 13);
        NativeModDrawer.SetColour(label, "Thistle");
    }
}

[OsuPatch, HarmonyPatch]
internal static class PositionStandardMods
{
    [HarmonyTargetMethod] private static MethodBase Target() => NativeModMenu.ModPosition;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Adjust the row coordinate on the stack, before Vector2 construction.
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == Newobj)
            {
                yield return new CodeInstruction(Ldarg_1);
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(PositionStandardMods), nameof(Y)));
            }
            yield return instruction;
        }
    }
    internal static float Y(float original, int row) => original + (StandardModLayout.IsStandard ? StandardModLayout.RowOffset(row) : 0);
}

[OsuPatch, HarmonyPatch]
internal static class ChangeStandardRuleset
{
    internal static readonly FieldInfo ModeField = MethodReader.GetInstructions(StandardModLayout.PlayMode)
        .Select(i => i.Operand).OfType<FieldInfo>().Single();
    [HarmonyTargetMethod]
    internal static MethodBase Target() => StandardModLayout.PlayMode.DeclaringType!
        .GetMethods(DifficultyControl.All).Single(m => m.IsStatic && m.ReturnType == typeof(void) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { StandardModLayout.PlayMode.ReturnType }) &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Stsfld && Equals(i.Operand, ModeField)));
    [HarmonyPrefix]
    private static void Before(object __0)
    {
        if (Convert.ToInt32(__0) == Convert.ToInt32(ModeField.GetValue(null))) return;
        // Clear the old mode's selections before the caller applies an incoming
        // replay's flags, rather than clearing its flags during validation.
        ModManager.ModStatus.Set(ModManager.ModStatus.Get() & ~(MirrorSettings.Flag | DifficultyControl.Flag));
        MirrorSettings.Selected = MirrorAxes.Horizontal;
        DifficultyControl.Selected = new DifficultySettings(null, null, null, null);
    }
}
