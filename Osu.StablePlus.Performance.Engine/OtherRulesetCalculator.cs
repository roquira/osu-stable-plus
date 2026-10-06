using System.Security.Cryptography;
using System.Text.Json;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Taiko.Difficulty;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Taiko.Objects;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;

namespace Osu.StablePlus.Performance.Engine;

internal sealed class OtherRulesetCalculator
{
    private readonly Dictionary<string, Entry> cache = new();
    internal static Ruleset RulesetFor(int mode) => mode switch
    {
        1 => new TaikoRuleset(),
        2 => new CatchRuleset(),
        3 => new ManiaRuleset(),
        _ => throw new InvalidDataException("Unsupported ruleset.")
    };

    internal CalculationResult Calculate(CalculationRequest request)
    {
        if (!request.NativeLazer && request.ConversionError != null)
            throw new InvalidDataException("Cannot verify stable conversion: " + request.ConversionError);
        if ((request.Mods & (1 << 21)) != 0)
            throw new InvalidDataException("Random's original seed is unavailable; pp cannot be reproduced reliably.");
        if (request.Difficulty.Any(v => v.HasValue) || request.Mirror != null || (request.Mods & int.MinValue) != 0)
            throw new InvalidDataException("DA and configurable Mirror are standard-only.");
        var bytes = File.ReadAllBytes(request.MapPath);
        var key = Convert.ToHexString(SHA256.HashData(bytes)) + JsonSerializer.Serialize(new
        {
            request.Mode,
            request.SourceMode,
            request.Mods,
            request.Rate,
            request.NativeLazer,
            request.Classic,
            request.WithoutRelax,
            CalculationRequest.EngineVersion,
            CalculationRequest.ProtocolVersion
        });
        if (!cache.TryGetValue(key, out var entry))
        {
            var ruleset = RulesetFor(request.Mode);
            using var reader = new LineBufferedReader(new MemoryStream(bytes));
            var map = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
            int source = map.BeatmapInfo.Ruleset.OnlineID;
            if (request.SourceMode.HasValue && source != request.SourceMode)
                throw new InvalidDataException("Beatmap source mode mismatch.");
            if (source != 0 && source != request.Mode) throw new InvalidDataException("Invalid ruleset conversion.");
            var working = new HeadlessMap(map);
            var mods = CreateMods(request, ruleset);
            var calculator = ruleset.CreateDifficultyCalculator(working);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var full = calculator.Calculate(mods, timeout.Token);
            var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
            entry = new Entry(ruleset, working, mods, calculator, full, playable);
            if (cache.Count >= 8) cache.Clear();
            cache[key] = entry;
        }
        if (!request.NativeLazer && request.ObjectStructure != null)
        {
            var mods = entry.Mods.Where(m => m is not ModMirror).ToArray();
            var playable = entry.Map.GetPlayableBeatmap(entry.Ruleset.RulesetInfo, mods);
            ValidateStructure(request.ObjectStructure, Structure(playable, request.Mode), request.Mode);
        }
        var attributes = entry.Full;
        if (request.Partial)
        {
            if (!request.ProgressTime.HasValue || !double.IsFinite(request.ProgressTime.Value))
                throw new InvalidDataException("Missing gameplay progression.");
            if (entry.Timed == null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                entry.Timed = entry.Calculator.CalculateTimed(entry.Mods, timeout.Token);
            }
            // Timed attributes are prefixes in beatmap order, not necessarily sorted by end time.
            var index = -1;
            for (int i = 0; i < entry.Timed.Count && entry.Timed[i].Time <= request.ProgressTime.Value; i++) index = i;
            if (index < 0) return new CalculationResult { Id = request.Id, Semantics = "stable/classic (partial play)" };
            attributes = entry.Timed[index].Attributes;
        }
        var result = new CalculationResult
        {
            Id = request.Id,
            Stars = attributes.StarRating,
            MaxCombo = attributes.MaxCombo,
            Hypothetical = request.WithoutRelax && (request.Mods & 128) != 0 && request.Mode != 3,
            Semantics = request.NativeLazer ? "lazer" : request.StableEstimate ? "stable estimate" : "stable/classic",
            MissMethod = "ruleset judgement statistics"
        };
        if (!double.IsFinite(result.Stars)) throw new InvalidDataException("Official calculator returned non-finite stars.");
        if (request.Maximum)
        {
            Dictionary<HitResult, int> maximum = request.Mode switch
            {
                1 => new() { [HitResult.Great] = entry.Playable.HitObjects.OfType<Hit>().Count() },
                2 => new()
                {
                    [HitResult.Great] = AllObjects(entry.Playable).Count(o => o is Fruit),
                    [HitResult.LargeTickHit] = AllObjects(entry.Playable).Count(o => o is Droplet && o is not TinyDroplet),
                    [HitResult.SmallTickHit] = AllObjects(entry.Playable).Count(o => o is TinyDroplet)
                },
                3 => new() { [HitResult.Perfect] = entry.Playable.HitObjects.Count },
                _ => throw new InvalidDataException("Unsupported ruleset.")
            };
            var maximumScore = new ScoreInfo(entry.Map.BeatmapInfo, entry.Ruleset.RulesetInfo)
            {
                Mods = entry.Mods,
                IsLegacyScore = true,
                LegacyTotalScore = null,
                Statistics = maximum,
                MaxCombo = attributes.MaxCombo,
                Accuracy = 1
            };
            ApplyPerformance(result, entry.Ruleset.CreatePerformanceCalculator()!.Calculate(maximumScore, attributes));
            result.MissMethod = "maximum";
            return result;
        }
        if (request.StarsOnly) return result;
        var stats = Statistics(request);
        if (stats.Values.Any(n => n < 0)) throw new InvalidDataException("Negative judgement counts.");
        ValidateStatistics(request, stats, entry.Playable);
        var score = new ScoreInfo(entry.Map.BeatmapInfo, entry.Ruleset.RulesetInfo)
        {
            Mods = entry.Mods,
            IsLegacyScore = !request.NativeLazer,
            LegacyTotalScore = null,
            Statistics = stats,
            MaxCombo = request.Combo,
            Accuracy = Accuracy(request.Mode, stats)
        };
        if (!stats.Any(p => p.Value != 0)) return result;
        var pp = entry.Ruleset.CreatePerformanceCalculator()!.Calculate(score, attributes);
        ApplyPerformance(result, pp);
        return result;
    }

    private static void ApplyPerformance(CalculationResult result, PerformanceAttributes pp)
    {
        result.Pp = pp.Total;
        if (pp is ManiaPerformanceAttributes mania) result.Difficulty = mania.Difficulty;
        if (pp is TaikoPerformanceAttributes taiko)
        {
            result.Difficulty = taiko.Difficulty; result.Accuracy = taiko.Accuracy;
            result.EstimatedUnstableRate = taiko.EstimatedUnstableRate;
        }
        if (new[] { result.Pp, result.Stars, result.Difficulty, result.Accuracy }.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("Official calculator returned a non-finite result.");
        if (result.EstimatedUnstableRate is { } ur && !double.IsFinite(ur))
            throw new InvalidDataException("Official calculator returned a non-finite unstable-rate estimate.");
    }

    internal static Mod[] CreateMods(CalculationRequest r, Ruleset ruleset)
    {
        int flags = r.Mods & int.MaxValue;
        if (r.Mode != 3) flags &= ~(1 << 30); // Mania Mirror is a native legacy mod.
        if (r.WithoutRelax && r.Mode != 3) flags &= ~128;
        var mods = ruleset.ConvertFromLegacyMods((osu.Game.Beatmaps.Legacy.LegacyMods)flags).ToList();
        if (!r.NativeLazer || r.Classic) mods.Add(ruleset.CreateMod<ModClassic>() ?? throw new InvalidDataException("Missing official Classic implementation."));
        foreach (var rate in mods.OfType<ModRateAdjust>()) rate.SpeedChange.Value = r.Rate;
        return mods.ToArray();
    }

    internal static Dictionary<HitResult, int> Statistics(CalculationRequest r)
    {
        if (r.NativeLazer && r.Statistics != null)
            return r.Statistics.ToDictionary(p => Enum.Parse<HitResult>(p.Key.Replace("_", ""), true), p => p.Value);
        var result = new Dictionary<HitResult, int> { [HitResult.Great] = r.N300, [HitResult.Miss] = r.Misses };
        if (r.Mode == 2)
        {
            result[HitResult.LargeTickHit] = r.N100; result[HitResult.SmallTickHit] = r.N50;
            result[HitResult.SmallTickMiss] = r.Katu;
        }
        else
        {
            result[HitResult.Ok] = r.N100;
            if (r.Mode == 3)
            {
                result[HitResult.Perfect] = r.Geki; result[HitResult.Good] = r.Katu; result[HitResult.Meh] = r.N50;
            }
            else result[HitResult.LargeBonus] = r.Geki + r.Katu;
        }
        return result;
    }

    private static void ValidateStatistics(CalculationRequest r, Dictionary<HitResult, int> s, IBeatmap map)
    {
        long Count(params HitResult[] results) => results.Sum(result => (long)s.GetValueOrDefault(result));
        long hits = r.Mode switch
        {
            3 => Count(HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh, HitResult.Miss),
            1 => Count(HitResult.Great, HitResult.Ok, HitResult.Miss),
            _ => Count(HitResult.Great, HitResult.LargeTickHit, HitResult.Miss, HitResult.LargeTickMiss)
        };
        long expected = r.Mode switch
        {
            3 => map.HitObjects.Count + (r.NativeLazer ? map.HitObjects.OfType<HoldNote>().Count() : 0),
            1 => map.HitObjects.OfType<Hit>().Count(),
            _ => AllObjects(map).Count(o => o is Fruit || o is Droplet && o is not TinyDroplet)
        };
        if (hits > expected || !r.Partial && !r.AllowPartial && hits != expected)
            throw new InvalidDataException("Incomplete or incompatible judgement counts for this ruleset's objects.");
        if (r.Mode == 2)
        {
            long tiny = Count(HitResult.SmallTickHit, HitResult.SmallTickMiss);
            long maximum = AllObjects(map).Count(o => o is TinyDroplet);
            if (tiny > maximum || !r.Partial && !r.AllowPartial && tiny != maximum)
                throw new InvalidDataException("Incomplete catch droplet statistics.");
        }
        // A partial stored non-standard score cannot be located reliably from
        // judgement counts alone (holds/rolls/streams are not root objects).
        if (!r.Partial && hits < expected && r.AllowPartial)
            throw new InvalidDataException("Partial score lacks recorded object progression.");
    }

    private static IEnumerable<HitObject> AllObjects(IBeatmap map)
    {
        IEnumerable<HitObject> Walk(HitObject o) => new[] { o }.Concat(o.NestedHitObjects.SelectMany(Walk));
        return map.HitObjects.SelectMany(Walk);
    }

    internal static double[][] Structure(IBeatmap map, int mode)
    {
        IEnumerable<HitObject> objects = mode == 2 ? AllObjects(map).Where(o => o is Fruit || o is Droplet && o is not TinyDroplet)
            : mode == 1 ? map.HitObjects.OfType<Hit>() : map.HitObjects;
        return objects.Select(o => new[] { o.StartTime, o.GetEndTime(), mode == 3 ? ((ManiaHitObject)o).Column :
            mode == 2 ? ((CatchHitObject)o).EffectiveX : 0d }).ToArray();
    }
    private static void ValidateStructure(double[][] native, double[][] official, int mode)
    {
        if (native.Length != official.Length || native.Any(o => o.Length != 3 || o.Any(v => !double.IsFinite(v))))
            throw new InvalidDataException($"Stable and official converted object counts differ ({native.Length} vs {official.Length}); pp unavailable.");
        double[][] Ordered(double[][] objects) => objects.OrderBy(o => o[0]).ThenBy(o => o[1]).ThenBy(o => o[2]).ToArray();
        native = Ordered(native); official = Ordered(official);
        for (int i = 0; i < native.Length; i++)
            // Stable truncates object times/positions to integer precision.
            if (Enumerable.Range(0, 3).Any(n => Math.Abs(native[i][n] - official[i][n]) > (n == 2 && mode == 3 ? 0 : 1.001)))
                throw new InvalidDataException("Stable and official converted object timing or positions differ; pp unavailable.");
    }

    internal static double Accuracy(int mode, Dictionary<HitResult, int> s)
    {
        int N(HitResult r) => s.GetValueOrDefault(r);
        double numerator, denominator;
        if (mode == 3)
        {
            numerator = 300d * (N(HitResult.Perfect) + N(HitResult.Great)) + 200d * N(HitResult.Good) + 100d * N(HitResult.Ok) + 50d * N(HitResult.Meh);
            denominator = 300d * (N(HitResult.Perfect) + N(HitResult.Great) + N(HitResult.Good) + N(HitResult.Ok) + N(HitResult.Meh) + N(HitResult.Miss));
        }
        else if (mode == 2)
        {
            numerator = N(HitResult.Great) + N(HitResult.LargeTickHit) + N(HitResult.SmallTickHit);
            denominator = numerator + N(HitResult.Miss) + N(HitResult.LargeTickMiss) + N(HitResult.SmallTickMiss);
        }
        else
        {
            numerator = 2d * N(HitResult.Great) + N(HitResult.Ok);
            denominator = 2d * (N(HitResult.Great) + N(HitResult.Ok) + N(HitResult.Miss));
        }
        return denominator > 0 ? numerator / denominator : 0;
    }

    private sealed class Entry(Ruleset ruleset, HeadlessMap map, Mod[] mods, DifficultyCalculator calculator, DifficultyAttributes full, IBeatmap playable)
    {
        internal readonly Ruleset Ruleset = ruleset;
        internal readonly HeadlessMap Map = map;
        internal readonly Mod[] Mods = mods;
        internal readonly DifficultyCalculator Calculator = calculator;
        internal readonly DifficultyAttributes Full = full;
        internal readonly IBeatmap Playable = playable;
        internal List<TimedDifficultyAttributes>? Timed;
    }
    private sealed class HeadlessMap(Beatmap map) : WorkingBeatmap(map.BeatmapInfo, null!)
    {
        protected override IBeatmap GetBeatmap() => map;
        public override Texture GetBackground() => null!;
        protected override Track GetBeatmapTrack() => null!;
        protected override ISkin GetSkin() => null!;
        public override Stream GetStream(string storagePath) => throw new NotSupportedException();
    }
}
