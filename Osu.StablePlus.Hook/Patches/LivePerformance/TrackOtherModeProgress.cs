using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.Audio;
using Osu.StablePlus.Stubs.GameModes.Play;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

// End-of-update capture observes the final native counters, including overrides
// which call the base judgement method before updating their own statistics.
internal static class TrackOtherModeProgress
{
    internal static readonly FieldInfo AudioTime = FindTime();
    private static FieldInfo FindTime()
    {
        var il = PatchProcessor.GetOriginalInstructions(CustomRateTiming.Target()).ToArray();
        return (FieldInfo)il.Select((instruction, index) => new { instruction, index }).Single(x =>
            x.instruction.opcode == System.Reflection.Emit.OpCodes.Ldsfld && x.instruction.operand is FieldInfo f &&
            f.DeclaringType == AudioEngine.SetCurrentPlaybackRate.Reference.DeclaringType &&
            x.index + 4 < il.Length && il[x.index + 3].opcode == System.Reflection.Emit.OpCodes.Sub && il[x.index + 4].LoadsConstant(1000)).instruction.operand;
    }
    internal static void Capture()
    {
        if (!PerformanceOptions.ShowPerformanceInGame.Value) return;
        if (Player.CurrentScore.Get() is { } score && StandardModLayout.ModeOf(score) != 0)
            PerformanceCalculator.Observe(score, (int)AudioTime.GetValue(null));
    }
}
