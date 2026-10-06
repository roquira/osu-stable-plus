using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Osu.StablePlus.Utils.IL;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using static System.Reflection.Emit.OpCodes;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

// Stable makes a fresh score while watching a replay and only copies the decoded
// frames into it. Transfer the source rate before any clock or ruleset setup.
[OsuPatch, HarmonyPatch]
internal static class CaptureGameplayRate
{
    [HarmonyTargetMethod]
    private static MethodBase Target() => Player.ResetScore.Reference;

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode != Stsfld || !Equals(instruction.operand, Player.CurrentScore.Reference)) continue;
            yield return new CodeInstruction(Call, AccessTools.Method(typeof(CaptureGameplayRate), nameof(Capture)));
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Expected one new gameplay score assignment.");
    }

    internal static void Capture()
    {
        if (Player.CurrentScore.Get() is { } score)
            RateControl.CaptureGameplayScore(score, Player.IsReplay.Invoke() ? Player.ReplayScore.Get() : null);
    }
}

[OsuPatch, HarmonyPatch]
internal static class CopyClonedReplayRate
{
    [HarmonyTargetMethod]
    private static MethodBase Target() => AccessTools.Method(Score.Class.Reference, "Clone");

    [HarmonyPostfix]
    private static void After(object __instance, object __result) => RateControl.Copy(__instance, __result);
}

[OsuPatch, HarmonyPatch]
internal static class PreserveReplaySeekRate
{
    [HarmonyTargetMethod]
    internal static MethodBase Target() => Player.Class.Reference.GetDeclaredMethods().Single(m =>
        m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsValueType &&
        !m.GetParameters()[0].ParameterType.IsPrimitive &&
        MethodReader.GetInstructions(m).Any(i => i.Opcode == Stsfld && Equals(i.Operand, Player.CurrentScore.Reference)));

    [HarmonyPrefix]
    private static void Before(out object? __state) => __state = Player.CurrentScore.Get();

    [HarmonyPostfix]
    private static void After(object? __state)
    {
        // Seeking replaces the score with a checkpoint, without going through Clone().
        if (__state != null && Player.CurrentScore.Get() is { } replacement)
            RateControl.Copy(__state, replacement);
    }
}

[OsuPatch, HarmonyPatch]
internal static class ReadReplayRate
{
    private struct ReadContext { internal bool Standalone; internal int Mode; }
    [HarmonyTargetMethod]
    private static MethodBase Target() => Score.ReadReplay.Reference;

    [HarmonyPrefix]
    private static void Before(object __instance, BinaryReader __0, out ReadContext __state)
    {
        var stream = __0.BaseStream;
        var standalone = stream.Position == 1 || (stream is FileStream file &&
            string.Equals(Path.GetExtension(file.Name), ".osr", StringComparison.OrdinalIgnoreCase));
        var mode = StandardModLayout.ModeOf(__instance);
        if (standalone && stream.CanSeek)
        {
            var position = stream.Position;
            try { stream.Position = 0; mode = stream.ReadByte(); }
            finally { stream.Position = position; }
        }
        __state = new ReadContext { Standalone = standalone, Mode = mode };
    }

    [HarmonyPostfix]
    private static void After(object __instance, BinaryReader __0, ReadContext __state)
    {
        var mods = RateControl.GetMods(__instance);
        var settings = new RateSettings(RateSettings.DefaultFor(mods), (mods & Nightcore) != 0);
        try
        {
            if (__state.Standalone)
                LivePerformance.OriginalScoreInfo.Capture(__instance, __0.BaseStream, Score.ReplayVersion.Get(__instance));
            var saved = ReplayRateMetadata.ReadTail(__0.BaseStream, Score.ReplayVersion.Get(__instance), mods, __state.Mode);
            if (__state.Standalone)
                saved = LivePerformance.OriginalScoreInfo.RestoreReplaySettings(__0.BaseStream, mods, __state.Mode) ?? saved;
            // Database rows without settings must remain eligible for the replay-file
            // lookup. Earlier patcher builds appended trailers to these rows as well;
            // consume those trailers so the following row remains readable.
            if (saved == null && !__state.Standalone) return;
            if (saved != null && !__state.Standalone && Score.ReplayVersion.Get(__instance) < 30000000)
                __0.BaseStream.Position += ReplayRateMetadata.StableTailLength;
            settings = saved ?? settings;
        }
        catch (Exception e)
        {
            // Do not play malformed/unsupported lazer settings as an unrelated
            // native mod combination. Keep the import readable, but flag playback.
            if (__state.Standalone && Score.ReplayVersion.Get(__instance) >= 30000001)
                settings = new RateSettings(settings.Speed, settings.AdjustPitch, unsupportedRulesetSettings: true);
            Console.WriteLine($"[Replay settings] Could not read replay metadata: {e.Message}");
        }
        RateControl.Remember(__instance, settings);
    }
}

[OsuPatch, HarmonyPatch]
internal static class WriteReplayRate
{
    [HarmonyTargetMethod]
    private static MethodBase Target() => Score.WriteReplay.Reference;

    [HarmonyPrefix]
    private static void Before(object __instance, BinaryWriter __0, out RateSettings? __state)
    {
        // The same serializer also writes records inside scores.db. Only standalone
        // replay files may have a trailer; database records must retain their layout.
        // Keep imported unsupported settings detectable after restart too. Fresh
        // non-standard plays have no custom metadata and stay entirely native.
        __state = __0.BaseStream.CanSeek && __0.BaseStream.Position == 0 &&
            (CustomRateOptions.IsEnabled(RateControl.GetMods(__instance)) || RateControl.ForScore(__instance).Difficulty != null ||
             RateControl.ForScore(__instance).Mirror != null || RateControl.ForScore(__instance).Scoring != null ||
             RateControl.ForScore(__instance).UnsupportedRulesetSettings)
            ? RateControl.ForScore(__instance) : null;
    }

    [HarmonyPostfix]
    private static void After(BinaryWriter __0, RateSettings? __state)
    {
        if (__state != null) ReplayRateMetadata.WriteTail(__0.BaseStream, __state);
    }
}
