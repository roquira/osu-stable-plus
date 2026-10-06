using System;
using System.Collections.Generic;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Stubs.Graphics;
using Osu.StablePlus.Utils.Extensions;
using static Osu.StablePlus.Hook.Patches.CustomStrings.CustomStrings;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

internal sealed class PerformanceOptions : PatchOptions
{
    public static readonly BindableWrapper<bool> ShowPerformanceInGame = new(BindableType.Bool, Settings.Default.ShowPerformanceInGame, Settings.Default.ShowPerformanceInGame);
    public static readonly BindableWrapper<bool> ShowPerformanceOnLeaderboard = new(BindableType.Bool, Settings.Default.ShowPerformanceOnLeaderboard, Settings.Default.ShowPerformanceOnLeaderboard);
    internal static readonly BindableWrapper<bool> WithoutRelax = new(BindableType.Bool, Settings.Default.EstimatePpWithoutRelax, Settings.Default.EstimatePpWithoutRelax);
    internal static readonly BindableWrapper<bool> WithoutAutopilot = new(BindableType.Bool, Settings.Default.EstimatePpWithoutAutopilot, Settings.Default.EstimatePpWithoutAutopilot);
    internal static readonly BindableWrapper<int> DecimalPlaces = new(BindableType.Object, Settings.Default.PerformanceDecimalPlaces, Settings.Default.PerformanceDecimalPlaces);
    internal static readonly OptionDropdown PrecisionDropdown = new(typeof(int));
    internal static int Precision => Math.Max(0, Math.Min(2, DecimalPlaces.Value));
    public override IEnumerable<object> CreateOptions()
    {
        yield return Check("Show PP during gameplay", "Official calculation for the current play.", ShowPerformanceInGame);
        yield return Check("Show PP on local leaderboards", "Calculate local scores with their recorded mod settings.", ShowPerformanceOnLeaderboard);
        yield return PrecisionDropdown.Constructor.Invoke([
            AddOsuString("PatcherPpPrecision", "PP decimal places"),
            new[] {
                pDropdownItem.Constructor.Invoke(["0", 0]),
                pDropdownItem.Constructor.Invoke(["1", 1]),
                pDropdownItem.Constructor.Invoke(["2", 2])
            }.ToType(pDropdownItem.Class.Reference),
            DecimalPlaces.Bindable, new EventHandler((_, _) => PerformanceDisplay.RefreshFormatting())
        ]);
        yield return Check("Estimate PP without Relax", "Show hypothetical PP using the same judgements with RX removed. Does not change star display preferences.", WithoutRelax);
        yield return Check("Estimate PP without Autopilot", "Show hypothetical PP using the same judgements with AP removed. Does not change star display preferences.", WithoutAutopilot);
    }
    private static object Check(string title, string tooltip, BindableWrapper<bool> value) =>
        OptionCheckbox.Constructor.Invoke([title, tooltip, value.Bindable, null]);
    public override void Load(Settings config)
    {
        ShowPerformanceInGame.Value = config.ShowPerformanceInGame;
        ShowPerformanceOnLeaderboard.Value = config.ShowPerformanceOnLeaderboard;
        WithoutRelax.Value = config.EstimatePpWithoutRelax;
        WithoutAutopilot.Value = config.EstimatePpWithoutAutopilot;
        DecimalPlaces.Value = Math.Max(0, Math.Min(2, config.PerformanceDecimalPlaces));
    }
    public override void Save(Settings config)
    {
        config.ShowPerformanceInGame = ShowPerformanceInGame.Value;
        config.ShowPerformanceOnLeaderboard = ShowPerformanceOnLeaderboard.Value;
        config.EstimatePpWithoutRelax = WithoutRelax.Value;
        config.EstimatePpWithoutAutopilot = WithoutAutopilot.Value;
        config.PerformanceDecimalPlaces = Precision;
    }
}
