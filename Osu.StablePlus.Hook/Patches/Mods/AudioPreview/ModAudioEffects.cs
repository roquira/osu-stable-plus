using System;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.Audio;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.AudioPreview;

/// <summary>
///     Handles applying and resetting the audio effects on the AudioEngine.
/// </summary>
internal static class ModAudioEffects
{
    /// <summary>
    ///     Applies the audio changes to the AudioEngine based on the current global mods.
    /// </summary>
    internal static void ApplyModEffects(bool clear = false)
    {
        if (!RateControl.IsSongSelection) return;
        StandardModLayout.ObserveSelection();
        ResetChanges();
        if (clear) return;

        var mods = ModManager.ModStatus.Get();
        var settings = CustomRateOptions.ForMods(mods);

        // NC always comes with DT
        if ((mods & Nightcore) > None)
            mods &= ~DoubleTime;

        switch (mods & (DoubleTime | Nightcore | HalfTime))
        {
            case DoubleTime:
                AudioEngine.Nightcore.Set(settings.AdjustPitch);
                UpdateAudioRate(rate => rate * settings.Speed);
                break;
            case Nightcore:
                AudioEngine.Nightcore.Set(true);
                UpdateAudioRate(rate => rate * settings.Speed);
                break;
            case HalfTime:
                AudioEngine.Nightcore.Set(settings.AdjustPitch);
                UpdateAudioRate(rate => rate * settings.Speed);
                break;
        }
    }

    /// <summary>
    ///     Resets the audio stream effects back to default.
    /// </summary>
    private static void ResetChanges()
    {
        AudioEngine.Nightcore.Set(false);
        UpdateAudioRate(_ => 100);
    }

    /// <summary>
    ///     Gets, modifies, and writes back the CurrentPlaybackRate on the AudioEngine.
    /// </summary>
    /// <param name="onModify">Rate transformer.</param>
    private static void UpdateAudioRate(Func<double, double> onModify)
    {
        var currentRate = AudioEngine.GetCurrentPlaybackRate.Invoke();
        var newRate = onModify.Invoke(currentRate);

        AudioEngine.SetCurrentPlaybackRate.Invoke(parameters: [newRate]);
    }
}
