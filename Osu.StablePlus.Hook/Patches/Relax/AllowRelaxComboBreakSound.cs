using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Utils.Extensions;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Hook.Patches.Relax;

/// <summary>
///     Changes the following code in
///     <c>osu.GameModes.Play.Rulesets.Ruleset:IncreaseScoreHit(IncreaseScoreType, HitObject)</c>
///     to enable the combo break sound during Relax* scores.
///     <br /><br />
///     From:
///     <code><![CDATA[
///         if (this.ComboCounter.HitCombo > 20 && !Player.Relaxing && !Player.Relaxing2)
///     ]]></code>
///     To:
///     <code><![CDATA[
///         if (this.ComboCounter.HitCombo > 20)
///     ]]></code>
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
internal static class AllowRelaxComboBreakSound
{
    // #=z04fOmc1I_BS0TV6TAo2QOUQvjceryuOcqoleWPg=:#=zSio4IZHzUUrC
    private static readonly OpCode[] Signature =
    [
        OpCodes.Ldarg_0,
        OpCodes.Ldfld,
        OpCodes.Callvirt,
        OpCodes.Ldc_I4_S,
        OpCodes.Ble_S,
        OpCodes.Ldsfld, // --------
        OpCodes.Brtrue_S, // All no-oped (4 inst)
        OpCodes.Ldsfld,
        OpCodes.Brtrue_S, // --------
    ];

    [UsedImplicitly]
    [HarmonyTargetMethod]
    internal static MethodBase Target() => OpCodeMatcher.FindMethodBySignature(null, Signature)!;

    [UsedImplicitly]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        instructions = instructions.NoopAfterSignature(
            Signature.Take(Signature.Length - 4).ToArray(),
            4
        );

        // Keep stable's original >20 combo gate, including its normal suppression
        // of consecutive misses after the combo has already been broken.
        return Osu.StablePlus.Hook.Patches.Mods.CustomRate.AdjustAssistedScore.Transpiler(
            Osu.StablePlus.Hook.Patches.Mods.CustomRate.ApplyDifficultyAdjust.Transpiler(instructions));
    }
}
