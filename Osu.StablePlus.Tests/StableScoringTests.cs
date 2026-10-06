using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class StableScoringTests
{
    [TestCase(1, 64, 1.7, 1.168)]
    [TestCase(2, 64, 1.7, 1.084)]
    [TestCase(3, 64, 2, 1)]
    [TestCase(1, 256, .5, .2)]
    [TestCase(2, 256, .99, .972)]
    [TestCase(3, 256, .5, 1d / 3)]
    [TestCase(3, 256, .75, .5)]
    [TestCase(3, 256, .99, .98)]
    public void ModeSpecificScoringCurves(int mode, int mods, double rate, double expected) =>
        Assert.That(StableScoreMath.RateMultiplier(mods, rate, mode), Is.EqualTo(expected).Within(1e-12));

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ModeDefaultRatesBypassArithmetic(int mode)
    {
        foreach (int mod in new[] { 64, 576, 256 })
            Assert.That(StableScoreMath.RelativeRate(mod, RateSettings.DefaultFor(mod), mode), Is.EqualTo(1));
        for (int rate = 101; rate <= 200; rate++)
            Assert.That(StableScoreMath.RateMultiplier(64, rate / 100d, mode), Is.EqualTo(StableScoreMath.RateMultiplier(576, rate / 100d, mode)));
    }

    [TestCase(64, 1.25, 1.06)]
    [TestCase(64, 1.5, 1.12)]
    [TestCase(576, 1.6, 1.144)]
    [TestCase(64, 2, 1.24)]
    [TestCase(256, 0.5, 0.2)]
    [TestCase(256, 0.75, 0.3)]
    [TestCase(256, 0.9, 0.72)]
    [TestCase(256, 0.99, 0.972)]
    [TestCase(0, 1.5, 1)]
    public void VanillaAnchoredCurve(int mods, double rate, double expected) =>
        Assert.That(StableScoreMath.RateMultiplier(mods, rate), Is.EqualTo(expected).Within(1e-12));

    [Test]
    public void EverySupportedStepIncreasesAndNightcoreMatches()
    {
        foreach (var mods in new[] { 64, 256 })
        {
            double previous = 0;
            for (int i = mods == 64 ? 101 : 50; i <= (mods == 64 ? 200 : 99); i++)
            {
                double current = StableScoreMath.RateMultiplier(mods, i / 100d);
                Assert.That(current, Is.GreaterThan(previous));
                if (mods == 64) Assert.That(StableScoreMath.RateMultiplier(576, i / 100d), Is.EqualTo(current));
                previous = current;
            }
        }
    }

    [TestCase(128)]
    [TestCase(8192)]
    public void AssistancePreservesDefaultsAndTruncatesEachAward(int assist)
    {
        foreach (var mods in new[] { 0, 64, 576, 256 })
            foreach (var award in new[] { 0, 10, 30, 50, 100, 300, 1100 })
                Assert.That(StableScoreMath.AssistedAward(award, assist | mods, RateSettings.DefaultFor(mods)), Is.EqualTo(award));
        Assert.That(StableScoreMath.AssistedAward(300, assist | 64, 1.6), Is.EqualTo(306));
        Assert.That(StableScoreMath.AssistedAward(10, assist | 64, 1.6), Is.EqualTo(10));
        Assert.That(StableScoreMath.AssistedAward(300, assist | 256, 0.9), Is.EqualTo(720));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ScoringSnapshotRoundTripsStableAndLazer(int version)
    {
        var settings = new RateSettings(1.7, false, new DifficultySettings(null, 0, null, 9), MirrorAxes.Both,
            new StableScoringSettings(version, 4));
        using var stream = new MemoryStream();
        ReplayRateMetadata.WriteTail(stream, settings);
        stream.Position = 0;
        var restored = ReplayRateMetadata.ReadTail(stream, 20260918, 64)!;
        Assert.That(restored.Scoring!.Version, Is.EqualTo(version));
        Assert.That(restored.Scoring.DifficultyFactor, Is.EqualTo(4));
        Assert.That(restored.Speed, Is.EqualTo(1.7));
        Assert.That(stream.Position, Is.Zero);
        var json = new JObject
        {
            ["mods"] = LazerReplayExport.CreateMods(64, settings),
            ["osu_patcher"] = new JObject { ["scoring"] = ReplayRateMetadata.ScoringJson(settings.Scoring!) }
        };
        Assert.That(ReplayRateMetadata.ReadLazerJson(json.ToString(), 64)!.Scoring!.Version, Is.EqualTo(version));
    }

    [Test]
    public void OldPatcherReplayRemainsLegacy()
    {
        using var stream = new MemoryStream();
        ReplayRateMetadata.WriteTail(stream, new RateSettings(1.7));
        stream.Position = 0;
        Assert.That(ReplayRateMetadata.ReadTail(stream, 20260918, 64)!.Scoring, Is.Null);
    }

    [Test]
    public void NewPreferenceDefaultsOn() => Assert.That(new Settings().AdjustStableScoring, Is.True);
}
