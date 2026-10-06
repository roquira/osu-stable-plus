using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Taiko.Objects;
using osu.Game.Scoring;
using osu.Game.Skinning;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;

namespace Osu.StablePlus.Performance.Engine.Tests;

[TestFixture]
public class OtherRulesetTests
{
    private Calculator engine = null!;
    private string directory = null!;
    [OneTimeSetUp]
    public void Setup()
    {
        engine = new Calculator();
        directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mode-pp-fixtures");
        Directory.CreateDirectory(directory);
        for (int mode = 0; mode <= 3; mode++)
        {
            var objects = Enumerable.Range(0, 160).Select(i =>
            {
                int x = 64 + (i % 4) * 128, time = 1000 + i * 150;
                if (mode == 3) return i % 7 == 0 ? $"{x},192,{time},128,0,{time + 350}:0:0:0:0:" : $"{x},192,{time},1,0,0:0:0:0:";
                return i % 17 == 0 ? $"{x},192,{time},2,0,L|{Math.Min(510, x + 100)}:192,2,100" : $"{x},192,{time},1,{(i % 4 == 0 ? 6 : 0)},0:0:0:0:";
            });
            File.WriteAllText(PathFor(mode), $"osu file format v14\n[General]\nMode:{mode}\n[Metadata]\nTitle:Modes\nArtist:Test\nCreator:Test\nVersion:Regression\n[Difficulty]\nHPDrainRate:5\nCircleSize:4\nOverallDifficulty:8\nApproachRate:9\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n" + string.Join("\n", objects));
        }
    }
    private string PathFor(int mode) => Path.Combine(directory, mode + ".osu");
    public static IEnumerable<TestCaseData> Rates()
    {
        foreach (int mode in new[] { 1, 2, 3 })
            foreach (bool convert in new[] { false, true })
                foreach (int flag in new[] { 0, 64, 576, 256 })
                    foreach (double rate in flag == 0 ? new[] { 1d } : flag == 256 ? new[] { .5, .75, .99 } : new[] { 1.01, 1.25, 1.5, 1.7, 2 })
                        yield return new TestCaseData(mode, convert, flag, rate);
    }
    [TestCaseSource(nameof(Rates))]
    public void MatchesIndependentOfficialScore(int mode, bool convert, int flags, double rate)
    {
        foreach (bool native in new[] { false, true })
        {
            Ruleset ruleset = mode switch { 1 => new TaikoRuleset(), 2 => new CatchRuleset(), _ => new ManiaRuleset() };
            string path = PathFor(convert ? 0 : mode);
            using var reader = new LineBufferedReader(File.OpenRead(path));
            var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
            var working = new ReferenceMap(decoded);
            // Independent mod construction: do not call the request adapter.
            var mods = new List<Mod>();
            if (!native) mods.Add(ruleset.CreateMod<ModClassic>()!);
            if ((flags & 1) != 0) mods.Add(ruleset.CreateMod<ModNoFail>()!);
            if ((flags & 2) != 0) mods.Add(ruleset.CreateMod<ModEasy>()!);
            if ((flags & 16) != 0) mods.Add(ruleset.CreateMod<ModHardRock>()!);
            if ((flags & 8) != 0) mods.Add(ruleset.CreateMod<ModHidden>()!);
            if ((flags & 128) != 0) mods.Add(ruleset.CreateMod<ModRelax>()!);
            if ((flags & (1 << 30)) != 0) mods.Add(ruleset.CreateMod<ModMirror>()!);
            if ((flags & (1 << 29)) != 0) mods.Add(new ModScoreV2());
            if ((flags & (1 << 15)) != 0) mods.Add(new osu.Game.Rulesets.Mania.Mods.ManiaModKey4());
            if ((flags & (1 << 18)) != 0) mods.Add(new osu.Game.Rulesets.Mania.Mods.ManiaModKey7());
            if ((flags & (64 | 256)) != 0)
            {
                ModRateAdjust mod = (flags & 256) != 0 ? ruleset.CreateMod<ModHalfTime>()! : (flags & 512) != 0 ? ruleset.CreateMod<ModNightcore>()! : ruleset.CreateMod<ModDoubleTime>()!;
                mod.SpeedChange.Value = rate; mods.Add(mod);
            }
            var map = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
            var attributes = ruleset.CreateDifficultyCalculator(working).Calculate(mods);
            var request = new CalculationRequest { MapPath = path, Mode = mode, SourceMode = convert ? 0 : mode, Mods = flags, Rate = rate, NativeLazer = native };
            var stats = new Dictionary<HitResult, int>();
            if (mode == 3)
            {
                int count = map.HitObjects.Count + (native ? map.HitObjects.OfType<HoldNote>().Count() : 0);
                request.Geki = count - 5; request.N300 = 1; request.Katu = 1; request.N100 = 1; request.N50 = 1; request.Misses = 1;
                stats[HitResult.Perfect] = count - 5; stats[HitResult.Great] = 1; stats[HitResult.Good] = 1; stats[HitResult.Ok] = 1; stats[HitResult.Meh] = 1; stats[HitResult.Miss] = 1;
            }
            else if (mode == 1)
            {
                int count = map.HitObjects.OfType<Hit>().Count();
                request.N300 = count - 2; request.N100 = 1; request.Misses = 1;
                stats[HitResult.Great] = count - 2; stats[HitResult.Ok] = 1; stats[HitResult.Miss] = 1;
            }
            else
            {
                IEnumerable<HitObject> Walk(HitObject o) => new[] { o }.Concat(o.NestedHitObjects.SelectMany(Walk));
                var all = map.HitObjects.SelectMany(Walk).ToArray();
                request.N300 = all.Count(o => o is Fruit) - 1; request.Misses = 1;
                request.N100 = all.Count(o => o is Droplet && o is not TinyDroplet);
                request.N50 = all.Count(o => o is TinyDroplet) - 1; request.Katu = 1;
                stats[HitResult.Great] = request.N300; stats[HitResult.Miss] = 1;
                stats[HitResult.LargeTickHit] = request.N100; stats[HitResult.SmallTickHit] = request.N50; stats[HitResult.SmallTickMiss] = 1;
            }
            request.Combo = attributes.MaxCombo / 2;
            if (native) request.Statistics = stats.ToDictionary(p => p.Key.ToString(), p => p.Value);
            double accuracy = mode == 3 ? ((native ? 305d : 300d) * request.Geki + 300d + 200d + 100d + 50d) / ((native ? 305d : 300d) * (request.Geki + 5)) :
                mode == 1 ? (2d * request.N300 + request.N100) / (2d * (request.N300 + request.N100 + request.Misses)) :
                (double)(request.N300 + request.N100 + request.N50) / (request.N300 + request.N100 + request.N50 + request.Misses + request.Katu);
            var score = new ScoreInfo(decoded.BeatmapInfo, ruleset.RulesetInfo) { Mods = mods.ToArray(), IsLegacyScore = !native, Statistics = stats, MaxCombo = request.Combo, Accuracy = accuracy };
            var expected = ruleset.CreatePerformanceCalculator()!.Calculate(score, attributes);
            var actual = engine.Calculate(request);
            Assert.That(actual.Stars, Is.EqualTo(attributes.StarRating).Within(1e-8));
            Assert.That(actual.Pp, Is.EqualTo(expected.Total).Within(1e-8));
            if (expected is osu.Game.Rulesets.Mania.Difficulty.ManiaPerformanceAttributes mania)
                Assert.That(actual.Difficulty, Is.EqualTo(mania.Difficulty).Within(1e-8));
            if (expected is osu.Game.Rulesets.Taiko.Difficulty.TaikoPerformanceAttributes taiko)
            {
                Assert.That(actual.Difficulty, Is.EqualTo(taiko.Difficulty).Within(1e-8));
                Assert.That(actual.Accuracy, Is.EqualTo(taiko.Accuracy).Within(1e-8));
                Assert.That(actual.EstimatedUnstableRate, Is.EqualTo(taiko.EstimatedUnstableRate));
            }
            Assert.That(double.IsFinite(actual.Pp), Is.True);
            request.Partial = true; request.ProgressTime = map.HitObjects.Max(o => o.GetEndTime()) + 1000;
            Assert.That(engine.Calculate(request).Pp, Is.EqualTo(actual.Pp).Within(1e-8));
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MaximumRequestsMatchPerfectOfficialScores(int mode)
    {
        Ruleset ruleset = mode switch { 1 => new TaikoRuleset(), 2 => new CatchRuleset(), _ => new ManiaRuleset() };
        using var reader = new LineBufferedReader(File.OpenRead(PathFor(mode)));
        var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        var working = new ReferenceMap(decoded);
        Mod[] mods = { ruleset.CreateMod<ModClassic>()! };
        var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var attributes = ruleset.CreateDifficultyCalculator(working).Calculate(mods);
        IEnumerable<HitObject> Walk(HitObject o) => new[] { o }.Concat(o.NestedHitObjects.SelectMany(Walk));
        var all = playable.HitObjects.SelectMany(Walk).ToArray();
        Dictionary<HitResult, int> statistics = mode switch
        {
            1 => new() { [HitResult.Great] = playable.HitObjects.OfType<Hit>().Count() },
            2 => new()
            {
                [HitResult.Great] = all.Count(o => o is Fruit),
                [HitResult.LargeTickHit] = all.Count(o => o is Droplet && o is not TinyDroplet),
                [HitResult.SmallTickHit] = all.Count(o => o is TinyDroplet)
            },
            _ => new() { [HitResult.Perfect] = playable.HitObjects.Count }
        };
        var score = new ScoreInfo(decoded.BeatmapInfo, ruleset.RulesetInfo)
        {
            Mods = mods,
            IsLegacyScore = true,
            Statistics = statistics,
            MaxCombo = attributes.MaxCombo,
            Accuracy = 1
        };
        var expected = ruleset.CreatePerformanceCalculator()!.Calculate(score, attributes);
        var actual = engine.Calculate(new CalculationRequest { MapPath = PathFor(mode), Mode = mode, SourceMode = mode, Maximum = true });
        Assert.That(actual.Stars, Is.EqualTo(attributes.StarRating).Within(1e-8));
        Assert.That(actual.Pp, Is.EqualTo(expected.Total).Within(1e-8));
        Assert.That(actual.MissMethod, Is.EqualTo("maximum"));
    }
    [TestCase(1, 129)]
    [TestCase(2, 129)]
    [TestCase(1, (1 << 29) | 64)]
    [TestCase(2, (1 << 29) | 64)]
    [TestCase(3, (1 << 29) | 64)]
    [TestCase(1, 8 | 16 | 64)]
    [TestCase(2, 8 | 16 | 64)]
    [TestCase(3, 8 | 16 | 64)]
    [TestCase(1, 2 | 256)]
    [TestCase(2, 2 | 256)]
    [TestCase(3, 2 | 256)]
    [TestCase(3, (1 << 15) | (1 << 30) | 64)]
    [TestCase(3, (1 << 18) | (1 << 30) | 64)]
    public void VanillaAndConversionModsMatch(int mode, int mods)
    {
        foreach (bool convert in new[] { false, true })
            MatchesIndependentOfficialScore(mode, convert, mods, (mods & 64) != 0 ? 1.7 : (mods & 256) != 0 ? .6 : 1);
    }
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TimedPrefixesUseObjectTimeRatherThanJudgementTotals(int mode)
    {
        Ruleset ruleset = mode switch { 1 => new TaikoRuleset(), 2 => new CatchRuleset(), _ => new ManiaRuleset() };
        using var reader = new LineBufferedReader(File.OpenRead(PathFor(mode)));
        var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        var working = new ReferenceMap(decoded);
        Mod[] mods = { ruleset.CreateMod<ModClassic>()!, ruleset.CreateMod<ModDoubleTime>()! };
        ((ModRateAdjust)mods[1]).SpeedChange.Value = 1.7;
        var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
        var timed = ruleset.CreateDifficultyCalculator(working).CalculateTimed(mods);
        var expected = timed.TakeWhile(t => t.Time <= 2200).Last().Attributes;
        var request = new CalculationRequest
        {
            MapPath = PathFor(mode),
            Mode = mode,
            Mods = 64,
            Rate = 1.7,
            Partial = true,
            ProgressTime = 2200,
            N300 = 1,
            Combo = 1
        };
        var before = engine.Calculate(request);
        Assert.That(before.Stars, Is.EqualTo(expected.StarRating).Within(1e-8));
        if (mode == 1) { request.Geki = 40; request.Katu = 20; }
        if (mode == 2) { request.N50 = 1; request.Katu = 1; }
        if (mode == 3) { request.N300 = 0; request.Geki = 1; }
        Assert.That(engine.Calculate(request).Stars, Is.EqualTo(before.Stars));
        request.ProgressTime = 0;
        Assert.That(engine.Calculate(request).Pp, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void UnknownProgressAndIncompatibleConversionsAreUnavailable(int mode)
    {
        var request = new CalculationRequest { MapPath = PathFor(mode), Mode = mode, N300 = 1, AllowPartial = true };
        Assert.Throws<InvalidDataException>(() => engine.Calculate(request));
        request.Partial = true;
        Assert.Throws<InvalidDataException>(() => engine.Calculate(request));
        request.ProgressTime = 1500;
        request.ConversionError = "test conversion mismatch";
        Assert.That(Assert.Throws<InvalidDataException>(() => engine.Calculate(request))!.Message, Does.Contain("conversion"));
        request.ConversionError = null;
        request.ObjectStructure = new[] { new[] { 0d, 0d, 0d } };
        Assert.That(Assert.Throws<InvalidDataException>(() => engine.Calculate(request))!.Message, Does.Contain("converted object"));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void EmptyMapsAndZeroStatisticsRemainFinite(int mode)
    {
        string path = PathFor(mode) + ".empty";
        File.WriteAllText(path, File.ReadAllText(PathFor(mode)).Split("[HitObjects]")[0] + "[HitObjects]\n");
        var request = new CalculationRequest { MapPath = path, Mode = mode };
        var result = engine.Calculate(request);
        Assert.That(result.Pp, Is.Zero); Assert.That(double.IsFinite(result.Stars), Is.True);
        request.Partial = true; request.ProgressTime = 0;
        Assert.That(engine.Calculate(request).Pp, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void EstimatePreferencesRespectRulesetCapabilities(int mode)
    {
        var request = new CalculationRequest
        {
            MapPath = PathFor(mode),
            Mode = mode,
            Mods = mode == 3 ? 0 : 128,
            Partial = true,
            ProgressTime = 25000,
            N300 = 1
        };
        var assisted = engine.Calculate(request);
        request.WithoutRelax = true; request.WithoutAutopilot = true;
        var hypothetical = engine.Calculate(request);
        Assert.That(hypothetical.Hypothetical, Is.EqualTo(mode != 3));
        request.Mods = 0; request.WithoutRelax = false; request.WithoutAutopilot = false;
        Assert.That(hypothetical.Pp, Is.EqualTo(engine.Calculate(request).Pp).Within(1e-8));
        if (mode == 3) Assert.That(assisted.Pp, Is.EqualTo(hypothetical.Pp));
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
