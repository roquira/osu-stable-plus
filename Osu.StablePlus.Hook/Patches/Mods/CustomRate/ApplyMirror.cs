using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.XNA;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[OsuPatch, HarmonyPatch]
internal static class ApplyMirror
{
    [HarmonyTargetMethod]
    internal static MethodBase Target() => DifficultyControl.Timing.DeclaringType!
        .GetMethods(DifficultyControl.All).Single(m => m.GetParameters().Length == 4 &&
            m.GetParameters()[0].ParameterType.FullName == "osu.GameplayElements.FileSection");
    private static readonly FieldInfo ManagerMods = Target().DeclaringType!.GetFields(DifficultyControl.All)
        .Single(f => f.FieldType == Score.EnabledMods.Reference.FieldType);
    private static readonly MethodInfo Unwrap = ManagerMods.FieldType.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == "op_Implicit" && m.ReturnType == Osu.StablePlus.Stubs.Root.Mods.Type.Reference);
    private static readonly int GameplayMode = Convert.ToInt32(Enum.Parse(Osu.StablePlus.Stubs.Root.GameBase.Mode.Reference.FieldType, "Play"));

    // Only the two Vector2 constructors adjacent to HR's 384-Y transform
    // represent object heads and slider control points. Storyboards are untouched.
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var il = instructions.ToList();
        var count = 0;
        for (var i = 0; i < il.Count; i++)
        {
            var instruction = il[i];
            if (instruction.opcode == Call && instruction.operand is ConstructorInfo c &&
                c.DeclaringType == Vector2.Class.Reference && il.Skip(Math.Max(0, i - 25)).Take(Math.Min(i, 25))
                    .Any(p => p.LoadsConstant(384) || p.LoadsConstant(384d)))
            {
                var manager = new CodeInstruction(Ldarg_0);
                manager.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                yield return manager;
                instruction.operand = AccessTools.Method(typeof(ApplyMirror), nameof(Initialize)).MakeGenericMethod(c.DeclaringType);
                count++;
            }
            yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException("Expected object-head and slider-point Mirror hooks.");
    }

    internal static void Initialize<T>(ref T point, float x, float y, object manager)
    {
        if (Osu.StablePlus.Stubs.Root.GameBase.Mode.Get() == GameplayMode && StandardModLayout.IsStandard &&
            (Convert.ToInt32(Unwrap.Invoke(null, [ManagerMods.GetValue(manager)])) & MirrorSettings.Flag) != 0)
        {
            var replay = Player.IsReplay.Invoke() ? Player.ReplayScore.Get() : null;
            var settings = replay != null ? RateControl.ForScore(replay) : RateControl.Current;
            var axes = settings.Mirror ?? MirrorAxes.Horizontal;
            x = (float)MirrorSettings.X(x, axes);
            y = (float)MirrorSettings.Y(y, axes);
        }
        point = (T)Vector2.Constructor.Invoke([x, y]);
    }
}
