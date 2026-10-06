using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

// Leave native drain methods and their return-value ABI untouched. Only replace
// the common caller's virtual dispatch, restoring the beatmap even on failure.
[OsuPatch, HarmonyPatch]
internal static class DifficultyDrainScope
{
    internal static readonly MethodInfo Update = FindUpdate();
    private static MethodInfo FindUpdate()
    {
        var drains = ApplyDifficultyAdjust.Readers().OfType<MethodInfo>()
            .Where(m => m.ReturnType == typeof(double)).ToArray();
        return drains.SelectMany(d => d.DeclaringType!.GetMethods(DifficultyControl.All)
            .Where(m => m.IsVirtual && m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
                MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, d))))
            .Select(m => m.GetBaseDefinition()).Distinct().Single();
    }

    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() => Update.DeclaringType!.GetMethods(DifficultyControl.All)
        .Where(m => m.GetMethodBody() != null && MethodReader.GetInstructions(m)
            .Any(i => i.Opcode == Callvirt && Equals(i.Operand, Update)));

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == Callvirt && Equals(instruction.operand, Update))
            {
                instruction.opcode = Call;
                instruction.operand = AccessTools.Method(typeof(DifficultyDrainScope), nameof(Calculate));
            }
            yield return instruction;
        }
    }

    private static void Calculate(object ruleset)
    {
        var map = DifficultyControl.CurrentBeatmap.Invoke(null, null);
        WithHp(map, map == null ? null : DifficultyControl.Read(map, 0), () =>
        {
            try { Update.Invoke(ruleset, null); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        });
    }

    internal static void WithHp(object? map, double? hp, Action calculate)
    {
        if (map == null || !hp.HasValue) { calculate(); return; }
        var field = DifficultyControl.Fields[0];
        var original = (float)field.GetValue(map);
        if (original == (float)hp.Value) { calculate(); return; }
        try { field.SetValue(map, (float)hp.Value); calculate(); }
        finally { field.SetValue(map, original); }
    }
}
