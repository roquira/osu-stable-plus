using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Play.Rulesets;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

[OsuPatch, HarmonyPatch]
internal static class TrackOnScoreHit
{
    [HarmonyTargetMethod] private static MethodBase Target() => Ruleset.OnIncreaseScoreHit.Reference;
    [HarmonyPostfix]
    private static void After(object __instance)
    {
        var score = Ruleset.CurrentScore.Get(__instance);
        if (score != null && Osu.StablePlus.Hook.Patches.Mods.CustomRate.StandardModLayout.ModeOf(score) == 0) PerformanceCalculator.Observe(score);
    }
}
