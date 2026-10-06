using System;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.Root;

namespace Osu.StablePlus.Tests;

public partial class StableIntegrationTests
{
    [TestCase(Mods.Relax, 1.5, 0.75)]
    [TestCase(Mods.Relax | Mods.DoubleTime, 1.25, 0.75)]
    [TestCase(Mods.Relax | Mods.DoubleTime, 1.7, 0.75)]
    [TestCase(Mods.Relax | Mods.HalfTime, 1.5, 0.6)]
    [TestCase(Mods.Relax2, 1.5, 0.75)]
    [TestCase(Mods.Relax2 | Mods.DoubleTime, 1.25, 0.75)]
    [TestCase(Mods.Relax2 | Mods.HalfTime, 1.5, 0.6)]
    public void AssistedScoringMenuKeepsNativeZeroMultiplierAndLabel(int mods, double dt, double ht)
    {
        var previousMods = ModManager.ModStatus.Get();
        var previousMode = ChangeStandardRuleset.ModeField.GetValue(null);
        var rates = new CustomRateOptions();
        var previous = new Settings(); rates.Save(previous);
        var enabled = StableScoringOptions.Enabled.Value;
        harmony.Patch(DifficultyControl.CurrentBeatmap, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(NoSelectedMapForMultiplierTest)));
        try
        {
            ChangeStandardRuleset.ModeField.SetValue(null, Enum.ToObject(ChangeStandardRuleset.ModeField.FieldType, 0));
            ModManager.ModStatus.Set(mods);
            rates.Load(new Settings { DoubleTimeSpeed = dt, HalfTimeSpeed = ht });
            StableScoringOptions.Enabled.Value = true;
            Assert.That(StableScoreMenu.Multiplier(0), Is.Zero);
            Assert.That(StableScoreMenu.Label("Score Multiplier: 0.00x"), Is.EqualTo("Score Multiplier: 0.00x"));
        }
        finally
        {
            StableScoringOptions.Enabled.Value = enabled;
            rates.Load(previous);
            ModManager.ModStatus.Set(previousMods);
            ChangeStandardRuleset.ModeField.SetValue(null, previousMode);
            harmony.Unpatch(DifficultyControl.CurrentBeatmap, AccessTools.Method(typeof(StableIntegrationTests), nameof(NoSelectedMapForMultiplierTest)));
        }
    }

    private static bool NoSelectedMapForMultiplierTest(ref object? __result) { __result = null; return false; }
}
