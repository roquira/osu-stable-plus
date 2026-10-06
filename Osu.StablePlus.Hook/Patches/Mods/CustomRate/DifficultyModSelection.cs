using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[OsuPatch, HarmonyPatch]
internal static class ResetDifficultyMod
{
    [HarmonyTargetMethod]
    internal static MethodBase Target() => ModManager.Class.Reference.GetMethods(DifficultyControl.All).Single(m =>
        m.IsStatic && m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
        MethodReader.GetInstructions(m).Count() <= 4 && MethodReader.GetInstructions(m)
            .Any(i => i.Opcode == Stsfld && Equals(i.Operand, ModManager.ModStatus.Reference)));
    [HarmonyPostfix] private static void After() { DifficultyControl.Disable(); MirrorSettings.Selected = MirrorAxes.Horizontal; }
}
