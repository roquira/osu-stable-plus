using System.Collections.Generic;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Stubs.Wrappers;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal sealed class ModLabelOptions : PatchOptions
{
    internal static readonly BindableWrapper<bool> RelaxAsRx = new(BindableType.Bool, Settings.Default.ShowRelaxAsRx, Settings.Default.ShowRelaxAsRx);
    internal static readonly BindableWrapper<bool> ShowRate = new(BindableType.Bool, Settings.Default.ShowRateInModLabels, Settings.Default.ShowRateInModLabels);
    internal static readonly BindableWrapper<bool> ShowDifficulty = new(BindableType.Bool, Settings.Default.ShowDifficultyInModLabels, Settings.Default.ShowDifficultyInModLabels);
    internal static readonly BindableWrapper<bool> ShowMirror = new(BindableType.Bool, Settings.Default.ShowMirrorInModLabels, Settings.Default.ShowMirrorInModLabels);
    public override IEnumerable<object> CreateOptions()
    {
        yield return OptionCheckbox.Constructor.Invoke(["Show Relax as RX on leaderboards",
            "Use the RX abbreviation in score mod lists.", RelaxAsRx.Bindable, null]);
        yield return OptionCheckbox.Constructor.Invoke(["Show DT/NC/HT rates in mod labels",
            "Show rate details in rankings, score tooltips and the song-selection mod text.", ShowRate.Bindable, null]);
        yield return OptionCheckbox.Constructor.Invoke(["Show DA adjustments in mod labels",
            "Show changed difficulty values in rankings, score tooltips and the song-selection mod text.", ShowDifficulty.Bindable, null]);
        yield return OptionCheckbox.Constructor.Invoke(["Show Mirror axes in mod labels",
            "Show H (horizontal), V (vertical) or B (both) in rankings, score tooltips and song-selection mod text.", ShowMirror.Bindable, null]);
    }
    public override void Load(Settings config)
    {
        RelaxAsRx.Value = config.ShowRelaxAsRx;
        ShowRate.Value = config.ShowRateInModLabels;
        ShowDifficulty.Value = config.ShowDifficultyInModLabels;
        ShowMirror.Value = config.ShowMirrorInModLabels;
    }
    public override void Save(Settings config)
    {
        config.ShowRelaxAsRx = RelaxAsRx.Value;
        config.ShowRateInModLabels = ShowRate.Value;
        config.ShowDifficultyInModLabels = ShowDifficulty.Value;
        config.ShowMirrorInModLabels = ShowMirror.Value;
    }
}
