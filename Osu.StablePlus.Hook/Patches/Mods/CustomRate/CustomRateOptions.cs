using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Osu.StablePlus.Hook.Patches.Mods.AudioPreview;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Utils;
using Osu.StablePlus.Stubs.Wrappers;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[UsedImplicitly]
internal sealed class CustomRateOptions : PatchOptions
{
    internal static RateSettings Selected { get; private set; } = new(1.5, false);
    internal static RateSettings SelectedHalfTime { get; private set; } = new(0.75, false);
    internal static RateSettings SelectedFor(int mods) => (mods & HalfTime) != 0 ? SelectedHalfTime : Selected;
    private static bool dirty;
    private static readonly BindableWrapper<bool> Pitch = new(BindableType.Bool, false, false);
    private static readonly BindableWrapper<bool> HalfTimePitch = new(BindableType.Bool, false, false);

    public override IEnumerable<object> CreateOptions()
    {
        Pitch.Value = Selected.AdjustPitch;
        yield return OptionCheckbox.Constructor.Invoke(["Adjust pitch with DT", "Raise audio pitch with DT speed. Nightcore always adjusts pitch.",
            Pitch.Bindable, new EventHandler((_, _) => { Set(Selected.Speed, Pitch.Value); Persist(); })]);
        HalfTimePitch.Value = SelectedHalfTime.AdjustPitch;
        yield return OptionCheckbox.Constructor.Invoke(["Adjust pitch with HT", "Lower audio pitch with HT speed.",
            HalfTimePitch.Bindable, new EventHandler((_, _) => { SetHalfTime(SelectedHalfTime.Speed, HalfTimePitch.Value); Persist(); })]);
    }

    internal static bool IsEnabled(int mods) => (mods & (DoubleTime | Nightcore | HalfTime)) != 0;

    internal static RateSettings NativeForMods(int mods) => new(RateSettings.DefaultFor(mods), (mods & Nightcore) != 0);

    internal static RateSettings ForMods(int mods) => !ModeRateTiming.Supports(StandardModLayout.ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null))) ? NativeForMods(mods) : new(SelectedFor(mods).Speed,
        (mods & Nightcore) != 0 || SelectedFor(mods).AdjustPitch,
        StandardModLayout.IsStandard && (mods & DifficultyControl.Flag) != 0 ? DifficultyControl.Selected : null,
        StandardModLayout.IsStandard && (mods & MirrorSettings.Flag) != 0 ? MirrorSettings.Selected : null);

    internal static void ObserveMods(int mods)
    {
        if ((mods & (DoubleTime | Nightcore)) == 0 && Selected.Speed != 1.5)
        {
            Selected = new RateSettings(1.5, Selected.AdjustPitch);
            dirty = true;
        }
        if ((mods & HalfTime) == 0 && SelectedHalfTime.Speed != 0.75)
        {
            SelectedHalfTime = new RateSettings(0.75, SelectedHalfTime.AdjustPitch);
            dirty = true;
        }
    }

    internal static void SetHalfTime(double speed, bool adjustPitch)
    {
        speed = RateSettings.NormalizeSpeed(speed, true);
        if (SelectedHalfTime.Speed == speed && SelectedHalfTime.AdjustPitch == adjustPitch) return;
        SelectedHalfTime = new RateSettings(speed, adjustPitch);
        dirty = true;
        if (RateControl.IsSongSelection && AudioPreviewOptions.Enabled.Value) ModAudioEffects.ApplyModEffects();
    }

    internal static void Set(double speed, bool adjustPitch)
    {
        speed = RateSettings.NormalizeSpeed(speed);
        if (Selected.Speed == speed && Selected.AdjustPitch == adjustPitch) return;
        Selected = new RateSettings(speed, adjustPitch);
        dirty = true;
        if (RateControl.IsSongSelection && AudioPreviewOptions.Enabled.Value) ModAudioEffects.ApplyModEffects();
    }

    internal static void Persist()
    {
        if (!dirty) return;
        try
        {
            var settings = new Settings();
            foreach (var options in Hook.PatchOptions) options.Save(settings);
            Settings.WriteToDisk(settings, Path.GetDirectoryName(OsuAssembly.Assembly.Location)!);
            dirty = false;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Custom DT] Unable to save settings: {e.Message}");
        }
    }

    public override void Load(Settings config)
    {
        Selected = new RateSettings(RateSettings.NormalizeSpeed(config.DoubleTimeSpeed), config.DoubleTimeAdjustPitch);
        SelectedHalfTime = new RateSettings(RateSettings.NormalizeSpeed(config.HalfTimeSpeed, true), config.HalfTimeAdjustPitch);
    }

    public override void Save(Settings config)
    {
        config.DoubleTimeSpeed = Selected.Speed;
        config.DoubleTimeAdjustPitch = Selected.AdjustPitch;
        config.HalfTimeSpeed = SelectedHalfTime.Speed;
        config.HalfTimeAdjustPitch = SelectedHalfTime.AdjustPitch;
    }
}
