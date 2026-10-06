using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Skinning;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;

namespace Osu.StablePlus.Performance.Engine;

public sealed class Calculator
{
    private readonly Dictionary<string, Entry> cache = new();
    private static readonly OsuRuleset Ruleset = new();
    private readonly OtherRulesetCalculator other = new();

    public Calculator() => Decoder.RegisterDependencies((Store)RuntimeHelpers.GetUninitializedObject(typeof(Store)));

    public CalculationResult Calculate(CalculationRequest request)
    {
        Validate(request);
        if (request.Mode != 0) return other.Calculate(request);
        var bytes = File.ReadAllBytes(request.MapPath);
        var key = Convert.ToHexString(SHA256.HashData(bytes)) + JsonSerializer.Serialize(new
        {
            request.Mods,
            request.Rate,
            request.Difficulty,
            request.Mirror,
            request.NativeLazer,
            request.WithoutRelax,
            request.WithoutAutopilot,
            request.ClassicNoSliderHeadAccuracy,
            CalculationRequest.EngineVersion
        });
        if (!cache.TryGetValue(key, out var entry))
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new LineBufferedReader(stream);
            var map = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
            if (map.BeatmapInfo.Ruleset.OnlineID != 0) throw new NotSupportedException("Only osu!standard is supported.");
            // Match patched stable: replace base fields first; official HR/EZ then act once.
            if (request.Difficulty[0] is double hp) map.Difficulty.DrainRate = (float)hp;
            if (request.Difficulty[1] is double cs) map.Difficulty.CircleSize = (float)cs;
            if (request.Difficulty[2] is double ar) map.Difficulty.ApproachRate = (float)ar;
            if (request.Difficulty[3] is double od) map.Difficulty.OverallDifficulty = (float)od;
            var working = new HeadlessMap(map);
            var mods = CreateMods(request);
            var calculator = new OsuDifficultyCalculator(Ruleset.RulesetInfo, working);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var attributes = (OsuDifficultyAttributes)calculator.Calculate(mods, timeout.Token);
            var playable = working.GetPlayableBeatmap(Ruleset.RulesetInfo, mods);
            var judgements = playable.HitObjects.SelectMany(o => o.NestedHitObjects.Prepend(o))
                .Select(o => o.CreateJudgement().MaxResult).ToArray();
            entry = new Entry(working, mods, calculator, attributes,
                judgements.Count(j => j == HitResult.LargeTickHit), judgements.Count(j => j == HitResult.SmallTickHit),
                judgements.Count(j => j == HitResult.SliderTailHit));
            if (cache.Count >= 8) cache.Clear();
            cache[key] = entry;
        }

        var hits = checked(request.N300 + request.N100 + request.N50 + request.Misses);
        var fullCount = entry.Full.HitCircleCount + entry.Full.SliderCount + entry.Full.SpinnerCount;
        bool partial = request.Partial || request.AllowPartial && hits < fullCount;
        var attributesForScore = entry.Full;
        if (partial && hits > 0)
        {
            if (entry.Timed == null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                entry.Timed = entry.Calculator.CalculateTimed(entry.Mods, timeout.Token);
            }
            if (hits > entry.Timed.Count) throw new InvalidDataException("More judgements than map objects.");
            attributesForScore = (OsuDifficultyAttributes)entry.Timed[hits - 1].Attributes;
        }
        var result = new CalculationResult
        {
            Id = request.Id,
            Stars = attributesForScore.StarRating,
            MaxCombo = attributesForScore.MaxCombo,
            Hypothetical = (request.WithoutRelax && (request.Mods & 128) != 0) ||
                          (request.WithoutAutopilot && (request.Mods & 8192) != 0),
            Semantics = request.NativeLazer ? "lazer" : request.StableEstimate ? "stable estimate" : "stable/classic"
        };
        if (partial && !request.Partial) result.Semantics += " (partial play)";
        if (!double.IsFinite(result.Stars)) throw new InvalidDataException("Official calculator returned non-finite stars.");
        if (request.Maximum)
        {
            var maximum = new ScoreInfo(entry.Map.BeatmapInfo, Ruleset.RulesetInfo)
            {
                Mods = entry.Mods,
                IsLegacyScore = true,
                MaxCombo = attributesForScore.MaxCombo,
                Accuracy = 1,
                Statistics = new Dictionary<HitResult, int> { [HitResult.Great] = fullCount }
            };
            var maximumPp = (OsuPerformanceAttributes)new OsuPerformanceCalculator().Calculate(maximum, attributesForScore);
            result.Pp = maximumPp.Total; result.Aim = maximumPp.Aim; result.Speed = maximumPp.Speed;
            result.Accuracy = maximumPp.Accuracy; result.Reading = maximumPp.Reading;
            result.Flashlight = maximumPp.Flashlight; result.EffectiveMisses = maximumPp.EffectiveMissCount;
            result.MissMethod = "maximum";
            if (new[] { result.Pp, result.Aim, result.Speed, result.Accuracy, result.Reading,
                        result.Flashlight, result.EffectiveMisses }.Any(v => !double.IsFinite(v)))
                throw new InvalidDataException("Official calculator returned a non-finite result.");
            return result;
        }
        if (request.StarsOnly || hits == 0) return result;
        var count = attributesForScore.HitCircleCount + attributesForScore.SliderCount + attributesForScore.SpinnerCount;
        if (hits > count || (!partial && hits != count)) throw new InvalidDataException("Incomplete or inconsistent score statistics.");
        if (request.NativeLazer && (partial || request.TailHits > entry.TailCount ||
            request.TickHits + request.TickMisses != entry.TickCount || request.SmallTickHits + request.SmallTickMisses != entry.SmallTickCount))
            throw new InvalidDataException("Incomplete lazer slider statistics.");
        // Custom point totals cannot be interpreted as vanilla ScoreV1 by the official miss estimator.
        var legacy = !request.NativeLazer && !partial && request.VanillaScoring && !result.Hypothetical &&
                     (request.Mods & (128 | 8192 | (1 << 29))) == 0 ? request.LegacyTotal : null;
        double numerator = 300d * request.N300 + 100d * request.N100 + 50d * request.N50;
        double denominator = 300d * hits;
        if (request.NativeLazer)
        {
            numerator += 30d * request.TickHits + 150d * request.TailHits + 10d * request.SmallTickHits;
            denominator += 30d * entry.TickCount + 150d * entry.TailCount + 10d * entry.SmallTickCount;
        }
        var score = new ScoreInfo(entry.Map.BeatmapInfo, Ruleset.RulesetInfo)
        {
            Mods = entry.Mods,
            IsLegacyScore = !request.NativeLazer,
            LegacyTotalScore = legacy,
            MaxCombo = request.Combo,
            Accuracy = numerator / denominator,
            Statistics = new Dictionary<HitResult, int>
            {
                [HitResult.Great] = request.N300,
                [HitResult.Ok] = request.N100,
                [HitResult.Meh] = request.N50,
                [HitResult.Miss] = request.Misses
            }
        };
        if (request.NativeLazer)
        {
            score.Statistics[HitResult.LargeTickHit] = request.TickHits;
            score.Statistics[HitResult.LargeTickMiss] = request.TickMisses;
            score.Statistics[HitResult.SliderTailHit] = request.TailHits;
            score.Statistics[HitResult.SmallTickHit] = request.SmallTickHits;
            score.Statistics[HitResult.SmallTickMiss] = request.SmallTickMisses;
        }
        var pp = (OsuPerformanceAttributes)new OsuPerformanceCalculator().Calculate(score, attributesForScore);
        result.Pp = pp.Total; result.Aim = pp.Aim; result.Speed = pp.Speed; result.Accuracy = pp.Accuracy;
        result.Reading = pp.Reading; result.Flashlight = pp.Flashlight; result.EffectiveMisses = pp.EffectiveMissCount;
        result.MissMethod = legacy.HasValue ? "legacy ScoreV1" : request.NativeLazer && request.ClassicNoSliderHeadAccuracy != true
            ? "combo-based (recorded slider statistics)" : "combo-based";
        if (new[] { result.Pp, result.Stars, result.Aim, result.Speed, result.Accuracy, result.Reading,
                    result.Flashlight, result.EffectiveMisses }.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("Official calculator returned a non-finite result.");
        return result;
    }

    public static Mod[] CreateMods(CalculationRequest request)
    {
        var flags = request.Mods & int.MaxValue & ~(1 << 30);
        if (request.WithoutRelax) flags &= ~128;
        if (request.WithoutAutopilot) flags &= ~8192;
        var mods = Ruleset.ConvertFromLegacyMods((osu.Game.Beatmaps.Legacy.LegacyMods)flags).ToList();
        if ((!request.NativeLazer || request.ClassicNoSliderHeadAccuracy.HasValue) && !mods.OfType<OsuModClassic>().Any())
            mods.Add(new OsuModClassic { NoSliderHeadAccuracy = { Value = !request.NativeLazer || request.ClassicNoSliderHeadAccuracy != false } });
        foreach (var mod in mods)
        {
            if (mod is ModRateAdjust rate) rate.SpeedChange.Value = request.Rate;
        }
        if (request.Mirror != null)
        {
            var mirror = new OsuModMirror();
            mirror.Reflection.Value = Enum.Parse<OsuModMirror.MirrorType>(request.Mirror, true);
            mods.Add(mirror);
        }
        return mods.ToArray();
    }

    private static void Validate(CalculationRequest r)
    {
        if (r.Mode < 0 || r.Mode > 3 || r.SourceMode < 0 || r.SourceMode > 3)
            throw new InvalidDataException("Invalid ruleset.");
        if (r.Protocol != CalculationRequest.ProtocolVersion) throw new InvalidDataException("Performance protocol mismatch.");
        if (r.Difficulty == null || r.Difficulty.Length != 4 || r.Difficulty.Any(v => v.HasValue && (!double.IsFinite(v.Value) || v < 0 || v > 10)))
            throw new InvalidDataException("Invalid difficulty settings.");
        if (!double.IsFinite(r.Rate) || r.Rate < 0.5 || r.Rate > 2) throw new InvalidDataException("Invalid clock rate.");
        if (new[] { r.N300, r.N100, r.N50, r.Misses, r.Geki, r.Katu, r.Combo, r.TickHits, r.TickMisses, r.TailHits, r.SmallTickHits, r.SmallTickMisses }.Any(n => n < 0) || r.LegacyTotal < 0)
            throw new InvalidDataException("Negative score statistics.");
        if (new System.IO.FileInfo(r.MapPath).Length > 32 * 1024 * 1024) throw new InvalidDataException("Beatmap exceeds calculation limit.");
    }

    private sealed class Entry(HeadlessMap map, Mod[] mods, OsuDifficultyCalculator calculator, OsuDifficultyAttributes full, int ticks, int smallTicks, int tails)
    {
        public readonly HeadlessMap Map = map;
        public readonly Mod[] Mods = mods;
        public readonly OsuDifficultyCalculator Calculator = calculator;
        public readonly OsuDifficultyAttributes Full = full;
        public readonly int TickCount = ticks;
        public readonly int SmallTickCount = smallTicks;
        public readonly int TailCount = tails;
        public List<TimedDifficultyAttributes>? Timed;
    }

    private sealed class Store : RulesetStore
    {
        private Store() { }
        public override IEnumerable<RulesetInfo> AvailableRulesets => new[] { Ruleset.RulesetInfo,
            new osu.Game.Rulesets.Taiko.TaikoRuleset().RulesetInfo,
            new osu.Game.Rulesets.Catch.CatchRuleset().RulesetInfo,
            new osu.Game.Rulesets.Mania.ManiaRuleset().RulesetInfo };
    }

    // Inherit the official conversion/defaults/stacking pipeline; no graphics or audio is created.
    private sealed class HeadlessMap(Beatmap source) : WorkingBeatmap(source.BeatmapInfo, null!)
    {
        protected override IBeatmap GetBeatmap() => source;
        public override Texture GetBackground() => null!;
        protected override Track GetBeatmapTrack() => null!;
        protected override ISkin GetSkin() => null!;
        public override Stream GetStream(string storagePath) => throw new NotSupportedException();
    }
}
