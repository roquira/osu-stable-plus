using System.Collections.Generic;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Stubs.Wrappers;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[UsedImplicitly]
internal sealed class StableScoringOptions : PatchOptions
{
    internal static readonly BindableWrapper<bool> Enabled = new(BindableType.Bool, Settings.Default.AdjustStableScoring, Settings.Default.AdjustStableScoring);

    public override IEnumerable<object> CreateOptions()
    {
        yield return OptionCheckbox.Constructor.Invoke(["Adjust stable scoring for custom mods",
            "Adjust ScoreV1 for custom rates in all modes and DA in osu!standard. Replays keep their saved scoring rules.",
            Enabled.Bindable, null]);
    }

    public override void Load(Settings config) => Enabled.Value = config.AdjustStableScoring;
    public override void Save(Settings config) => config.AdjustStableScoring = Enabled.Value;
}
