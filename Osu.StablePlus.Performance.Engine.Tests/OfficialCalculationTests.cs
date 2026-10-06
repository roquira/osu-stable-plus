using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Skinning;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;

namespace Osu.StablePlus.Performance.Engine.Tests;

[TestFixture]
public class OfficialCalculationTests
{
    private Calculator engine = null!;
    private string map = null!;
    [OneTimeSetUp]
    public void Setup()
    {
        engine = new Calculator();
        map = Path.Combine(TestContext.CurrentContext.WorkDirectory, "official-pp-fixture.osu");
        File.WriteAllText(map, """
            osu file format v14
            [General]
            Mode:0
            [Metadata]
            Title:Performance regression
            Artist:Test
            Creator:Test
            Version:Sliders
            [Difficulty]
            HPDrainRate:5
            CircleSize:4
            OverallDifficulty:8
            ApproachRate:9
            SliderMultiplier:1.4
            SliderTickRate:1
            [TimingPoints]
            0,500,4,1,0,100,1,0
            [HitObjects]
            128,192,1000,1,0,0:0:0:0:
            256,192,2000,2,0,L|396:192,3,280
            400,100,6000,1,0,0:0:0:0:
            128,200,7000,2,0,L|268:200,1,140
            256,192,9000,8,0,10000,0:0:0:0:
            """);
    }

    public static IEnumerable<TestCaseData> Matrix()
    {
        foreach (int flags in new[] { 0, 1, 2, 8, 16, 32, 64, 128, 256, 576, 1024, 2048, 4096, 8192, 16416, 1 << 29, 8 | 64 | 128, 1 | 8192, 16 | 8 | 1024 })
            foreach (bool lazer in new[] { false, true })
                yield return new TestCaseData(flags, (flags & 256) != 0 ? .75 : (flags & 64) != 0 ? 1.5 : 1d, -1d, lazer, false, false, "");
        foreach (int flag in new[] { 64, 576, 256 })
            foreach (double rate in flag == 256 ? new[] { .5, .75, .99 } : new[] { 1.01, 1.25, 1.5, 1.7, 2 })
                foreach (double cs in new[] { 0d, 4d, 10d })
                    yield return new TestCaseData(flag | 8, rate, cs, false, false, false, "Both");
        foreach (int flags in new[] { 128, 8192, 128 | 1, 8192 | 1, 128 | 64, 8192 | 256 })
            foreach (bool strip in new[] { false, true })
                yield return new TestCaseData(flags, (flags & 256) != 0 ? .5 : (flags & 64) != 0 ? 1.7 : 1d, 3d, false, strip, strip, "Vertical");
        foreach (string mirror in new[] { "Horizontal", "Vertical", "Both" })
            foreach (int flags in new[] { 16, 2, 16 | 64 })
                yield return new TestCaseData(flags, (flags & 64) != 0 ? 1.25 : 1d, 1.4, false, false, false, mirror);
    }

    [TestCaseSource(nameof(Matrix))]
    public void MatchesDirectOfficialScore(int flags, double rate, double cs, bool lazer, bool stripRx, bool stripAp, string mirror)
    {
        var request = new CalculationRequest
        {
            MapPath = map,
            Mods = flags,
            Rate = rate,
            NativeLazer = lazer,
            N300 = 3,
            N100 = 1,
            Misses = 1,
            Combo = 3,
            WithoutRelax = stripRx,
            WithoutAutopilot = stripAp,
            Mirror = mirror.Length == 0 ? null : mirror,
            Difficulty = cs < 0 ? new double?[4] : new double?[] { 6, cs, 9.5, 9 }
        };
        var reference = Reference(request);
        var result = engine.Calculate(request);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Stars, Is.EqualTo(reference.Item1.StarRating).Within(1e-8));
        Assert.That(result.Pp, Is.EqualTo(reference.Item2.Total).Within(1e-8));
        Assert.That(result.Aim, Is.EqualTo(reference.Item2.Aim).Within(1e-8));
        Assert.That(result.Speed, Is.EqualTo(reference.Item2.Speed).Within(1e-8));
        Assert.That(result.Accuracy, Is.EqualTo(reference.Item2.Accuracy).Within(1e-8));
        Assert.That(result.Reading, Is.EqualTo(reference.Item2.Reading).Within(1e-8));
        Assert.That(result.Flashlight, Is.EqualTo(reference.Item2.Flashlight).Within(1e-8));
        Assert.That(result.EffectiveMisses, Is.EqualTo(reference.Item2.EffectiveMissCount).Within(1e-8));
    }

    // Build the comparison through official score/beatmap APIs, without the engine's mod adapter or cache.
    private (OsuDifficultyAttributes, OsuPerformanceAttributes) Reference(CalculationRequest r)
    {
        using var stream = File.OpenRead(map);
        using var reader = new LineBufferedReader(stream);
        var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        decoded.Difficulty.DrainRate = (float)(r.Difficulty[0] ?? 5);
        decoded.Difficulty.CircleSize = (float)(r.Difficulty[1] ?? 4);
        decoded.Difficulty.ApproachRate = (float)(r.Difficulty[2] ?? 9);
        decoded.Difficulty.OverallDifficulty = (float)(r.Difficulty[3] ?? 8);
        var ruleset = new OsuRuleset(); var working = new ReferenceMap(decoded);
        var mods = new List<Mod>();
        if (!r.NativeLazer || r.ClassicNoSliderHeadAccuracy.HasValue)
            mods.Add(new OsuModClassic { NoSliderHeadAccuracy = { Value = !r.NativeLazer || r.ClassicNoSliderHeadAccuracy != false } });
        void Add(int flag, Mod mod) { if ((r.Mods & flag) != 0) mods.Add(mod); }
        Add(1, new OsuModNoFail()); Add(2, new OsuModEasy()); Add(8, new OsuModHidden()); Add(16, new OsuModHardRock());
        if ((r.Mods & 16384) != 0) mods.Add(new OsuModPerfect()); else Add(32, new OsuModSuddenDeath());
        if ((r.Mods & 512) != 0) mods.Add(new OsuModNightcore { SpeedChange = { Value = r.Rate } });
        else if ((r.Mods & 64) != 0) mods.Add(new OsuModDoubleTime { SpeedChange = { Value = r.Rate } });
        if ((r.Mods & 256) != 0) mods.Add(new OsuModHalfTime { SpeedChange = { Value = r.Rate } });
        if (!r.WithoutRelax) Add(128, new OsuModRelax());
        if (!r.WithoutAutopilot) Add(8192, new OsuModAutopilot());
        Add(1024, new OsuModFlashlight()); Add(2048, new OsuModAutoplay()); Add(4096, new OsuModSpunOut()); Add(1 << 29, new ModScoreV2());
        if (r.Mirror != null) mods.Add(new OsuModMirror { Reflection = { Value = Enum.Parse<OsuModMirror.MirrorType>(r.Mirror) } });
        var attributes = (OsuDifficultyAttributes)new OsuDifficultyCalculator(ruleset.RulesetInfo, working).Calculate(mods);
        var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        if (r.Maximum)
        {
            var maximumScore = new ScoreInfo(decoded.BeatmapInfo, ruleset.RulesetInfo)
            {
                Mods = mods.ToArray(),
                IsLegacyScore = true,
                MaxCombo = attributes.MaxCombo,
                Accuracy = 1,
                Statistics = new Dictionary<HitResult, int> { [HitResult.Great] = playable.HitObjects.Count }
            };
            return (attributes, (OsuPerformanceAttributes)new OsuPerformanceCalculator().Calculate(maximumScore, attributes));
        }
        var maximum = playable.HitObjects.SelectMany(o => o.NestedHitObjects.Prepend(o)).Select(o => o.CreateJudgement().MaxResult).ToArray();
        int ticks = maximum.Count(j => j == HitResult.LargeTickHit), small = maximum.Count(j => j == HitResult.SmallTickHit), tails = maximum.Count(j => j == HitResult.SliderTailHit);
        if (r.NativeLazer) { r.TickMisses = 1; r.TickHits = ticks - 1; r.TailHits = Math.Min(1, tails); r.SmallTickHits = Math.Max(0, small - 1); r.SmallTickMisses = Math.Min(1, small); }
        double numerator = 300 * r.N300 + 100 * r.N100 + 50 * r.N50, denominator = 300 * 5;
        var statistics = new Dictionary<HitResult, int> { [HitResult.Great] = r.N300, [HitResult.Ok] = r.N100, [HitResult.Meh] = r.N50, [HitResult.Miss] = r.Misses };
        if (r.NativeLazer)
        {
            statistics[HitResult.LargeTickHit] = r.TickHits; statistics[HitResult.LargeTickMiss] = r.TickMisses; statistics[HitResult.SliderTailHit] = r.TailHits;
            statistics[HitResult.SmallTickHit] = r.SmallTickHits; statistics[HitResult.SmallTickMiss] = r.SmallTickMisses;
            numerator += 30 * r.TickHits + 150 * r.TailHits + 10 * r.SmallTickHits; denominator += 30 * ticks + 150 * tails + 10 * small;
        }
        var score = new ScoreInfo(decoded.BeatmapInfo, ruleset.RulesetInfo) { Mods = mods.ToArray(), IsLegacyScore = !r.NativeLazer, Statistics = statistics, MaxCombo = r.Combo, Accuracy = numerator / denominator };
        if (r.VanillaScoring) score.LegacyTotalScore = r.LegacyTotal;
        return (attributes, (OsuPerformanceAttributes)new OsuPerformanceCalculator().Calculate(score, attributes));
    }

    [Test]
    public void MaximumRequestMatchesPerfectOfficialScore()
    {
        var request = new CalculationRequest
        {
            MapPath = map,
            Mods = 8 | 64,
            Rate = 1.25,
            Maximum = true,
            Difficulty = new double?[] { 6, 3.8, 9.5, 9 }
        };
        var expected = Reference(request);
        var actual = engine.Calculate(request);
        Assert.That(actual.Stars, Is.EqualTo(expected.Item1.StarRating).Within(1e-8));
        Assert.That(actual.Pp, Is.EqualTo(expected.Item2.Total).Within(1e-8));
        Assert.That(actual.MissMethod, Is.EqualTo("maximum"));
    }

    [Test]
    public void FinalTimedMatchesFullAndIgnoresAdjustedScoreTotal()
    {
        var r = new CalculationRequest { MapPath = map, Mods = 64 | 128, Rate = 1.7, N300 = 5, Combo = 10, LegacyTotal = 1000000, VanillaScoring = false };
        var full = engine.Calculate(r); r.Partial = true;
        var timed = engine.Calculate(r);
        Assert.That(timed.Pp, Is.EqualTo(full.Pp).Within(1e-8));
        Assert.That(timed.MissMethod, Is.EqualTo("combo-based"));
        r.N300 = 2; r.Combo = 2;
        Assert.That(double.IsFinite(engine.Calculate(r).Pp), Is.True);
        r.N300 = 0; r.Combo = 0;
        Assert.That(engine.Calculate(r).Pp, Is.Zero);
    }

    [Test]
    public void VanillaTotalIsUsedOnlyWithTheCorrectContract()
    {
        var r = new CalculationRequest { MapPath = map, N300 = 3, N100 = 1, Misses = 1, Combo = 3, LegacyTotal = 5000, VanillaScoring = true };
        Assert.That(engine.Calculate(r).MissMethod, Is.EqualTo("legacy ScoreV1"));
        Assert.That(engine.Calculate(r).Pp, Is.EqualTo(Reference(r).Item2.Total).Within(1e-8));
        r.Partial = true; Assert.That(engine.Calculate(r).MissMethod, Is.EqualTo("combo-based"));
        r.Partial = false; r.Mods = 128; r.WithoutRelax = true;
        Assert.That(engine.Calculate(r).MissMethod, Is.EqualTo("combo-based"));
    }

    [TestCase(true, 0)]
    [TestCase(false, 0)]
    [TestCase(true, 128)]
    [TestCase(true, 8192)]
    public void NativeClassicRetainsItsOwnSliderJudgements(bool classicAccuracy, int mods)
    {
        var r = new CalculationRequest { MapPath = map, NativeLazer = true, ClassicNoSliderHeadAccuracy = classicAccuracy, Mods = mods, N300 = 3, N100 = 1, Misses = 1, Combo = 3 };
        var expected = Reference(r);
        var actual = engine.Calculate(r);
        Assert.That(actual.Pp, Is.EqualTo(expected.Item2.Total).Within(1e-8));
        Assert.That(actual.Accuracy, Is.EqualTo(expected.Item2.Accuracy).Within(1e-8));
        Assert.That(actual.Semantics, Is.EqualTo("lazer"));
    }

    [TestCase(5, 0, 0, 0, 14)]
    [TestCase(0, 5, 0, 0, 4)]
    [TestCase(0, 0, 5, 0, 4)]
    [TestCase(0, 0, 0, 5, 0)]
    public void AccuracyAndMissBoundariesMatchOfficial(int great, int ok, int meh, int miss, int combo)
    {
        var r = new CalculationRequest { MapPath = map, N300 = great, N100 = ok, N50 = meh, Misses = miss, Combo = combo, Mods = 1 | 64, Rate = 1.25 };
        Assert.That(engine.Calculate(r).Pp, Is.EqualTo(Reference(r).Item2.Total).Within(1e-8));
    }

    [Test]
    public void EmptyMapsAndIncompleteLocalScoresAreFinite()
    {
        var empty = map + ".empty.osu";
        File.WriteAllText(empty, File.ReadAllText(map).Split("[HitObjects]")[0] + "[HitObjects]\n");
        Assert.That(engine.Calculate(new CalculationRequest { MapPath = empty }).Pp, Is.Zero);
        var r = new CalculationRequest { MapPath = map, N300 = 2, Combo = 2, AllowPartial = true, VanillaScoring = true, LegacyTotal = 4000 };
        var failed = engine.Calculate(r); r.Partial = true;
        Assert.That(failed.Pp, Is.EqualTo(engine.Calculate(r).Pp).Within(1e-8));
        Assert.That(failed.Semantics, Does.Contain("partial")); Assert.That(failed.MissMethod, Is.EqualTo("combo-based"));
    }

    [Test]
    public void ChangedMapContentInvalidatesCacheAndOtherModesAreRejected()
    {
        var copy = map + ".changed.osu";
        File.Copy(map, copy, true);
        var r = new CalculationRequest { MapPath = copy, StarsOnly = true };
        var before = engine.Calculate(r).Stars;
        File.WriteAllText(copy, File.ReadAllText(copy).Replace("CircleSize:4", "CircleSize:0").Replace("0,500,4", "0,250,4"));
        Assert.That(engine.Calculate(r).Stars, Is.Not.EqualTo(before));
        File.WriteAllText(copy, File.ReadAllText(copy).Replace("Mode:0", "Mode:3"));
        Assert.Throws<NotSupportedException>(() => engine.Calculate(r));
    }

    public static IEnumerable<TestCaseData> IndividualDifficultyCases()
    {
        for (int index = 0; index < 4; index++)
            foreach (double value in new[] { 0d, 5d, 10d })
                foreach (int mods in new[] { 0, 2, 16, 64, 256 })
                    yield return new TestCaseData(index, value, mods);
    }

    [TestCaseSource(nameof(IndividualDifficultyCases))]
    public void IndividualDifficultyBoundariesComposeOnce(int index, double value, int flags)
    {
        var r = new CalculationRequest { MapPath = map, Mods = flags | int.MinValue | (1 << 30), Mirror = "Both", Rate = flags == 64 ? 2 : flags == 256 ? .5 : 1, N300 = 3, N100 = 1, Misses = 1, Combo = 3 };
        r.Difficulty[index] = value;
        var expected = Reference(r);
        var actual = engine.Calculate(r);
        Assert.That(actual.Stars, Is.EqualTo(expected.Item1.StarRating).Within(1e-8));
        Assert.That(actual.Pp, Is.EqualTo(expected.Item2.Total).Within(1e-8));
    }

    [Test]
    public void InvalidInputsFailWithoutPoisoningTheCalculator()
    {
        var r = new CalculationRequest { MapPath = map, Rate = double.NaN, StarsOnly = true };
        Assert.Throws<InvalidDataException>(() => engine.Calculate(r)); r.Rate = 1; r.Difficulty[1] = -1;
        Assert.Throws<InvalidDataException>(() => engine.Calculate(r)); r.Difficulty[1] = 0; r.N300 = -1;
        Assert.Throws<InvalidDataException>(() => engine.Calculate(r)); r.N300 = 0;
        Assert.That(engine.Calculate(r).Stars, Is.GreaterThan(0));
    }

    private sealed class ReferenceMap(Beatmap map) : WorkingBeatmap(map.BeatmapInfo, null!)
    {
        protected override IBeatmap GetBeatmap() => map;
        public override Texture GetBackground() => null!;
        protected override Track GetBeatmapTrack() => null!;
        protected override ISkin GetSkin() => null!;
        public override Stream GetStream(string path) => throw new NotSupportedException();
    }
}
