using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;
using static Osu.StablePlus.Stubs.Root.Mods;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Hook.Patches.Relax;

/// <summary>Allow NF with RX/AP without changing the other mod incompatibilities.</summary>
[OsuPatch, HarmonyPatch]
internal static class AllowNoFailAssistance
{
    [HarmonyPostfix]
    private static void After(int __state)
    {
        if (StandardModLayout.IsStandard)
            ModManager.ModStatus.Set(ModManager.ModStatus.Get() | __state);
        else DifficultyControl.Disable();
        if (!MirrorSettings.Enabled) MirrorSettings.Selected = MirrorAxes.Horizontal;
        if ((ModManager.ModStatus.Get() & (Easy | HardRock)) != 0)
            Osu.StablePlus.Hook.Patches.Mods.CustomRate.DifficultyControl.Disable();
    }

    [HarmonyTargetMethod]
    internal static MethodBase Target() => ModManager.Class.Reference
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Single(m =>
            m.ReturnType == typeof(void) && m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType == Osu.StablePlus.Stubs.Root.Mods.Type.Reference &&
            MethodReader.GetInstructions(m).Count(i => i.Opcode == Stsfld &&
                Equals(i.Operand, ModManager.ModStatus.Reference)) > 20);

    [HarmonyPrefix]
    private static void Before(object __0, out int __state)
    {
        var mode = StandardModLayout.ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null));
        var changed = Convert.ToInt32(__0);
        if (mode == 0 && changed == MirrorSettings.Flag) ModManager.ModStatus.Set(ModManager.ModStatus.Get() & ~HardRock);
        if (mode == 0 && changed == HardRock) ModManager.ModStatus.Set(ModManager.ModStatus.Get() & ~MirrorSettings.Flag);
        __state = mode == 0 ? ModManager.ModStatus.Get() & MirrorSettings.Flag : 0;
        if (mode == 0 && changed == Osu.StablePlus.Hook.Patches.Mods.CustomRate.DifficultyControl.Flag)
            ModManager.ModStatus.Set(ModManager.ModStatus.Get() & ~(Easy | HardRock));
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var il = instructions.ToList();
        // Each native case is a sequence of `ModStatus &= ~incompatibleMod`.
        // Match whole sequences so the same masks in SD, autoplay or other cases remain intact.
        RemoveConflicts([SuddenDeath, Perfect, Relax2, Osu.StablePlus.Stubs.Root.Mods.Relax], [2, 3]);
        RemoveConflicts([Relax2, NoFail, SuddenDeath, Perfect], [1]);
        RemoveConflicts([SpunOut, Osu.StablePlus.Stubs.Root.Mods.Relax, NoFail, SuddenDeath, Perfect], [2]);
        return il;

        void RemoveConflicts(int[] mods, int[] remove)
        {
            var starts = Enumerable.Range(0, Math.Max(0, il.Count - mods.Length * 4 + 1))
                .Where(start => mods.Select((mod, index) => new { mod, offset = start + index * 4 }).All(p =>
                    il[p.offset].opcode == Ldsfld && Equals(il[p.offset].operand, ModManager.ModStatus.Reference) &&
                    il[p.offset + 1].LoadsConstant(~p.mod) && il[p.offset + 2].opcode == And &&
                    il[p.offset + 3].opcode == Stsfld && Equals(il[p.offset + 3].operand, ModManager.ModStatus.Reference)))
                .ToArray();
            if (starts.Length != 1) throw new InvalidOperationException("Could not uniquely locate NF/RX/AP incompatibility checks.");
            foreach (var index in remove)
            {
                var instruction = il[starts[0] + index * 4 + 1];
                instruction.opcode = Ldc_I4_M1;
                instruction.operand = null;
            }
        }
    }
}
