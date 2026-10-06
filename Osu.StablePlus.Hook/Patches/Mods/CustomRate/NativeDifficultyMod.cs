using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using System.Collections.Generic;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[OsuPatch, HarmonyPatch]
internal static class DifficultyModTexture
{
    [HarmonyTargetMethod] private static MethodBase Target() => NativeModMenu.ModTexture;
    [HarmonyPrefix]
    private static void Before(ref string __0)
    {
        if (__0 == "selection-mod-" + DifficultyControl.Flag) __0 = "selection-mod-difficultyadjust";
    }
    [HarmonyPostfix]
    private static void After(string __0, ref object __result)
    {
        if (__0 == "selection-mod-difficultyadjust" && __result == null)
            __result = NativeModMenu.DifficultyTexture();
    }
}

[OsuPatch, HarmonyPatch]
internal static class DifficultyModKeyboard
{
    [HarmonyTargetMethod]
    internal static MethodBase Target() => NativeModMenu.Menu.GetMethods(DifficultyControl.All).Single(m =>
        m.IsStatic && m.ReturnType == Osu.StablePlus.Stubs.Root.Mods.Type.Reference && m.GetParameters().Length == 1 &&
        m.GetParameters()[0].ParameterType.FullName == "Microsoft.Xna.Framework.Input.Keys");
    private static readonly int Z = Convert.ToInt32(Enum.Parse(((MethodInfo)Target()).GetParameters()[0].ParameterType, "Z"));
    private static readonly int X = Convert.ToInt32(Enum.Parse(((MethodInfo)Target()).GetParameters()[0].ParameterType, "X"));
    internal static readonly FieldInfo Shift = MethodReader.GetInstructions(Target()).Where(i => i.Opcode == Ldsfld)
        .Select(i => i.Operand).OfType<FieldInfo>().Distinct().Single(f => f.FieldType == typeof(bool));
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == Ret)
            {
                var key = new CodeInstruction(Ldarg_0);
                key.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                yield return key;
                yield return new CodeInstruction(Call, AccessTools.Method(typeof(DifficultyModKeyboard), nameof(Map)));
            }
            yield return instruction;
        }
    }
    internal static int Map(int original, int key) => !StandardModLayout.IsStandard || !(bool)Shift.GetValue(null) ? original :
        key == Z ? DifficultyControl.Flag : key == X ? MirrorSettings.Flag : original;
}

[OsuPatch, HarmonyPatch]
internal static class DifficultyModHud
{
    internal static IEnumerable<MethodBase> DisplayMethods() => ModManager.Class.Reference.Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(DifficultyControl.All)).Where(m => m.GetMethodBody() != null &&
        MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, Osu.StablePlus.Stubs.Root.Mods.Type.Reference)) &&
        MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, NativeModMenu.ModTexture)) &&
        MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, AccessTools.Method(typeof(Enum), nameof(Enum.GetValues)))));

    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() => DisplayMethods()
        .Where(m => m != Osu.StablePlus.Stubs.GameModes.Play.Player.OnLoadComplete.Reference);

    internal static Array DisplayValues(Type type)
    {
        var original = Enum.GetValues(type);
        if (type != Osu.StablePlus.Stubs.Root.Mods.Type.Reference) return original;
        // Composite masks also pass stable's "any active bit" test, but have
        // no icon. In the gameplay HUD they still advance X by 10 and the
        // animation delay by 500 ms. DA comes after those masks, exposing the
        // otherwise invisible gaps. Only individual mods belong in this list.
        var individual = original.Cast<object>().Where(value =>
        {
            var bits = unchecked((uint)Convert.ToInt32(value));
            return bits == 0 || (bits & (bits - 1)) == 0;
        }).ToArray();
        var values = Array.CreateInstance(type, individual.Length + 1);
        for (var i = 0; i < individual.Length; i++) values.SetValue(individual[i], i);
        values.SetValue(Enum.ToObject(type, DifficultyControl.Flag), individual.Length);
        return values;
    }

    internal static int DisplayMods(object score)
    {
        if (StandardModLayout.ModeOf(score) != 0) return RateControl.GetMods(score) & ~DifficultyControl.Flag;
        var settings = RateControl.ForScore(score);
        return RateControl.GetMods(score) | (settings.Difficulty != null ? DifficultyControl.Flag : 0);
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var instruction = list[i];
            if (instruction.Calls(AccessTools.Method(typeof(Enum), nameof(Enum.GetValues))))
                instruction.operand = AccessTools.Method(typeof(DifficultyModHud), nameof(DisplayValues));
            // DA uses the high bit. Signed >0 flag checks silently discard it.
            if (i >= 2 && list[i - 2].opcode == And && list[i - 1].opcode == Ldc_I4_0 && instruction.opcode == Cgt)
                instruction.opcode = Cgt_Un;
            if (instruction.opcode == Ldfld && Equals(instruction.operand, Score.EnabledMods.Reference) &&
                i + 1 < list.Count && list[i + 1].opcode == Call)
            {
                instruction.opcode = Call;
                instruction.operand = AccessTools.Method(typeof(DifficultyModHud), nameof(DisplayMods));
                list[i + 1].opcode = Nop;
                list[i + 1].operand = null;
            }
            yield return instruction;
        }
    }
}
