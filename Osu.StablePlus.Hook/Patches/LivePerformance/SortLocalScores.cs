using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

/// <summary>Restore descending score order before stable assigns local ranks and score gaps.</summary>
[OsuPatch, HarmonyPatch]
internal static class SortLocalScores
{
    internal static readonly MethodInfo Comparison = Score.Class.Reference.GetMethod(nameof(IComparable.CompareTo), [Score.Class.Reference])
        ?? throw new InvalidOperationException("Cannot locate the native score comparison.");
    private static readonly IComparer<object> Comparer = Comparer<object>.Create((left, right) =>
        (int)Comparison.Invoke(left, [right]));

    [HarmonyTargetMethod]
    internal static MethodBase Target()
    {
        var row = LocalScoreRateDisplay.Target();
        var builder = SongSelection.ChoseBestSortMode.Reference.DeclaringType!
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Single(method => MethodReader.GetInstructions(method).Any(instruction => Equals(instruction.Operand, row)));
        var list = typeof(List<>).MakeGenericType(Score.Class.Reference);
        return MethodReader.GetInstructions(builder).Select(instruction => instruction.Operand).OfType<MethodInfo>()
            .Distinct().Single(method => method.IsStatic && method.ReturnType == list &&
                method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType == typeof(string) &&
                method.GetParameters()[1].ParameterType.IsEnum);
    }

    [HarmonyPostfix]
    private static void After(object? __result)
    {
        if (__result is not IList scores || scores.Count < 2) return;
        // The reader returns a filtered copy, so the saved database list is untouched.
        // Native comparison uses the displayed total, then the earlier timestamp.
        var ordered = scores.Cast<object>().OrderBy(score => score, Comparer).ToArray();
        for (var index = 0; index < ordered.Length; index++) scores[index] = ordered[index];
    }
}
