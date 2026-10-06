using System.Collections.Generic;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Stubs.Wrappers;
using NativeMods = Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal sealed class StarRatingOptions : PatchOptions
{
    internal static readonly BindableWrapper<bool> ReduceRelax = new(BindableType.Bool, Settings.Default.ApplyRelaxStarReduction, Settings.Default.ApplyRelaxStarReduction);
    internal static readonly BindableWrapper<bool> ReduceAutopilot = new(BindableType.Bool, Settings.Default.ApplyAutopilotStarReduction, Settings.Default.ApplyAutopilotStarReduction);

    // Only filter the private display calculation; keep the selected and recorded mods intact.
    internal static int CalculationMods(int mods)
    {
        if (!ReduceRelax.Value) mods &= ~NativeMods.Relax;
        if (!ReduceAutopilot.Value) mods &= ~NativeMods.Relax2;
        return mods;
    }

    public override IEnumerable<object> CreateOptions()
    {
        yield return OptionCheckbox.Constructor.Invoke(["Apply Relax (RX) star reduction",
            "Include Relax's difficulty reduction in the song-selection star rating.", ReduceRelax.Bindable, null]);
        yield return OptionCheckbox.Constructor.Invoke(["Apply Autopilot (AP) star reduction",
            "Include Autopilot's difficulty reduction in the song-selection star rating.", ReduceAutopilot.Bindable, null]);
    }

    public override void Load(Settings config)
    {
        ReduceRelax.Value = config.ApplyRelaxStarReduction;
        ReduceAutopilot.Value = config.ApplyAutopilotStarReduction;
    }

    public override void Save(Settings config)
    {
        config.ApplyRelaxStarReduction = ReduceRelax.Value;
        config.ApplyAutopilotStarReduction = ReduceAutopilot.Value;
    }
}
