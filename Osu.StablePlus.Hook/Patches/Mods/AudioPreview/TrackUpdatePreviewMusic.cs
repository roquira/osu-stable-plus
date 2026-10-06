using System.Reflection;
using System;
using System.Collections.Generic;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.Audio;

namespace Osu.StablePlus.Hook.Patches.Mods.AudioPreview;

/// <summary>
///     Hooks the place where preview audio gets loaded to apply our mod audio effects.
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
public class TrackUpdatePreviewMusic
{
    [UsedImplicitly]
    [HarmonyTargetMethod]
    private static MethodBase Target() => AudioEngine.LoadAudioForPreview.Reference;

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(AudioEngine.GetCurrentPlaybackRate.Reference))
            {
                instruction.operand = AccessTools.Method(typeof(TrackUpdatePreviewMusic), nameof(ContinuationRate));
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("Expected one preview continuation rate check.");
    }

    // Only the continuation decision treats live mod rates as normal playback.
    // Native same-track, continuePlayback and end-of-track checks remain intact.
    internal static double ContinuationRate() => AudioPreviewOptions.Enabled.Value && RateControl.IsSongSelection
        ? 100d : AudioEngine.GetCurrentPlaybackRate.Invoke();

    [HarmonyPostfix]
    [UsedImplicitly]
    private static void After()
    {
        if (!AudioPreviewOptions.Enabled.Value)
            return;

        ModAudioEffects.ApplyModEffects();
    }
}
