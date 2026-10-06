using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.Helpers;

namespace Osu.StablePlus.Hook.Patches.CustomStrings;

/// <summary>
///     Extend <c>Enum.GetName</c> for our additional localisation keys only.
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
internal static class AddEnumValueNames
{
    [UsedImplicitly]
    [HarmonyTargetMethod]
    private static MethodBase Target() => typeof(Enum).Method(nameof(Enum.GetName));

    [HarmonyPrefix]
    [UsedImplicitly]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private static bool Before(
        ref string __result,
        [HarmonyArgument(0)] Type enumType,
        [HarmonyArgument(1)] object? value)
    {
        // This is a framework-wide hook. In particular, storyboard SampleSet.All
        // is -1, so converting unrelated enums to uint breaks hitsound triggers.
        if (value == null || enumType != OsuString.Class.Reference) return true;
        if (value is not Enum && value is not byte && value is not sbyte &&
            value is not short && value is not ushort && value is not int &&
            value is not uint && value is not long && value is not ulong)
            return true;

        uint valueIdx;
        try { valueIdx = Convert.ToUInt32(value); }
        catch (OverflowException) { return true; }

        if (!CustomStrings.OsuStringNames.TryGetValue(valueIdx, out var name)) return true;
        __result = name;
        return false;
    }
}
