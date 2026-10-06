using System;
using System.IO;
using System.Runtime.CompilerServices;
using Osu.StablePlus.Stubs.Audio;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Stubs.Root;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal static class RateControl
{
    private static readonly ConditionalWeakTable<object, RateSettings> Scores = new();
    private static readonly ConditionalWeakTable<byte[], RateSettings> ReplayBuffers = new();
    private static readonly Obfuscated ModsStub = new(Stubs.Root.Mods.Type.Reference);

    // Derive the enum value from stable instead of relying on its ordinal.
    internal static bool IsSongSelection => GameBase.Mode.Get() == Convert.ToInt32(
        Enum.Parse(GameBase.Mode.Reference.FieldType, "SelectPlay"));

    internal static int GetMods(object score) => ModsStub.GetValue.Invoke<int>(Score.EnabledMods.Get(score));

    internal static void Remember(object score, RateSettings settings)
    {
        if (StandardModLayout.ModeOf(score) == 0 && (settings.Difficulty != null || settings.Mirror != null))
        {
            var wrapper = Score.EnabledMods.Reference.FieldType;
            var conversion = System.Linq.Enumerable.Single(wrapper.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public),
                m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
            Score.EnabledMods.Set(score, conversion.Invoke(null, [Enum.ToObject(Stubs.Root.Mods.Type.Reference,
                GetMods(score) | (settings.Difficulty != null ? DifficultyControl.Flag : 0) |
                (settings.Mirror != null ? MirrorSettings.Flag : 0))]));
        }
        Scores.Remove(score);
        Scores.Add(score, settings);
        var buffer = Score.ReplayData.Get(score);
        if (buffer != null)
        {
            ReplayBuffers.Remove(buffer);
            ReplayBuffers.Add(buffer, settings);
        }
    }

    internal static RateSettings ForScore(object score)
    {
        if (Scores.TryGetValue(score, out var settings)) return settings;
        var buffer = Score.ReplayData.Get(score);
        if (buffer != null && ReplayBuffers.TryGetValue(buffer, out settings))
        {
            Remember(score, settings);
            return settings;
        }

        // Scores loaded from scores.db lazily copy replayData from a separate Score instance.
        // This also covers old imports made before injection; reopen the original .osr once.
        var filename = (string)Score.GetReplayFilename.Reference.Invoke(score, null);
        if (File.Exists(filename))
        {
            settings = ReadFile(filename, GetMods(score));
        }
        else
        {
            // A missing/legacy replay must never borrow the menu's rate. New plays get
            // an explicit snapshot in CaptureGameplayScore, before their audio starts.
            return new RateSettings(RateSettings.DefaultFor(GetMods(score)), (GetMods(score) & Nightcore) != 0);
        }
        Remember(score, settings);
        return settings;
    }

    internal static void CaptureGameplayScore(object score, object? replay)
    {
        var settings = replay != null && Score.IsLoadedReplay.Get(replay)
            ? ForScore(replay)
            : ModeRateTiming.Supports(StandardModLayout.ModeOf(score)) ? CustomRateOptions.ForMods(ModManager.ModStatus.Get())
            : CustomRateOptions.NativeForMods(GetMods(score));
        if (replay == null)
            settings = settings.WithScoring(new StableScoringSettings(NativeStableScoring.Supports(StandardModLayout.ModeOf(score)) && StableScoringOptions.Enabled.Value &&
                ModeRateTiming.Supports(StandardModLayout.ModeOf(score)) &&
                (ModManager.ModStatus.Get() & StableScoreMath.ScoreV2) == 0 ? StandardModLayout.ModeOf(score) == 0 ? 1 : StableScoringSettings.CurrentVersion : 0));
        Remember(score, settings);
    }

    internal static void Copy(object source, object destination)
    {
        if (Scores.TryGetValue(source, out var settings)) Remember(destination, settings);
    }

    private static RateSettings ReadFile(string filename, int mods)
    {
        var lazer = false;
        try
        {
            using var stream = File.OpenRead(filename);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            reader.ReadByte();
            lazer = reader.ReadInt32() >= 30000001;
            stream.Position = 0;
            var settings = ReplayRateMetadata.ReadReplay(stream);
            stream.Position = 0;
            var mode = stream.ReadByte();
            settings = LivePerformance.OriginalScoreInfo.RestoreReplaySettings(stream, mods, mode) ?? settings;
            if (settings != null) return settings;
            lazer = false; // Valid native/default mods require no settings record.
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Custom DT] Unable to read saved replay settings: {e.Message}");
        }
        return new RateSettings(RateSettings.DefaultFor(mods), (mods & Nightcore) != 0, unsupportedRulesetSettings: lazer);
    }

    internal static RateSettings Current => Player.CurrentScore.Get() is { } score
        ? ForScore(score) : new RateSettings();

    internal static double GameplayPercent() => GameplayMultiplier() * 100;
    internal static double GameplayMultiplier() => Player.CurrentScore.Get() is { } score && !ModeRateTiming.Supports(StandardModLayout.ModeOf(score))
        ? RateSettings.DefaultFor(GetMods(score)) : Current.Speed;

    internal static void SetGameplayRate(double percent)
    {
        var score = Player.CurrentScore.Get();
        var pitch = AudioEngine.Nightcore.Get();
        try
        {
            if (score != null && ModeRateTiming.Supports(StandardModLayout.ModeOf(score)) && CustomRateOptions.IsEnabled(GetMods(score)))
                AudioEngine.Nightcore.Set(ForScore(score).AdjustPitch);
            AudioEngine.SetCurrentPlaybackRate.Invoke(null, [percent]);
        }
        finally
        {
            // Adjust pitch must not enable Nightcore's other effects.
            AudioEngine.Nightcore.Set(pitch);
        }
    }
}
