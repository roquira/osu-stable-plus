using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Osu.StablePlus.Performance;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

internal static class PerformanceRequests
{
    internal static readonly FieldInfo[] AllCounts = FindCounts();
    internal static readonly FieldInfo[] Counts = { AllCounts[0], AllCounts[1], AllCounts[2], AllCounts[5] };
    private static FieldInfo[] FindCounts()
    {
        var header = MethodReader.GetInstructions(Score.ReadReplay.Reference).Select(i => i.Operand)
            .OfType<MethodInfo>().First(m => m.DeclaringType == Score.Class.Reference && m.IsPublic &&
                m.GetParameters().Length == 1 && typeof(BinaryReader).IsAssignableFrom(m.GetParameters()[0].ParameterType));
        var instructions = MethodReader.GetInstructions(header).ToArray();
        var fields = instructions.Select((instruction, index) => new { instruction, index })
            .Where(item => item.instruction.Operand is MethodInfo call && call.Name == nameof(BinaryReader.ReadUInt16))
            .Select(item => (FieldInfo)instructions.Skip(item.index + 1)
                .First(i => i.Opcode == System.Reflection.Emit.OpCodes.Stfld).Operand).ToArray();
        if (fields.Length != 7) throw new InvalidOperationException("Expected six judgements and max combo in the replay header.");
        return fields.Take(6).ToArray();
    }

    internal static CalculationRequest Capture(object score)
    {
        if (Counts.Length != 4) throw new InvalidOperationException("Expected four standard judgement counters.");
        var mode = StandardModLayout.ModeOf(score);
        var map = Score.Beatmap.Get(score) ?? DifficultyControl.CurrentBeatmap.Invoke(null, null);
        var settings = RateControl.ForScore(score);
        if (settings.UnsupportedRulesetSettings) throw new NotSupportedException("Unsupported replay settings.");
        int mods = RateControl.GetMods(score);
        var request = new CalculationRequest
        {
            MapPath = Beatmap.GetBeatmapPath(map!) ?? throw new IOException("Missing beatmap."),
            Mode = mode,
            SourceMode = StandardModLayout.NativeModeOf(map!),
            Mods = mods,
            Rate = CustomRateOptions.IsEnabled(mods) ? settings.Speed : 1,
            Difficulty = settings.Difficulty?.Values.ToArray() ?? new double?[4],
            Mirror = settings.Mirror?.ToString(),
            N300 = Convert.ToInt32(Counts[0].GetValue(score)),
            N100 = Convert.ToInt32(Counts[1].GetValue(score)),
            N50 = Convert.ToInt32(Counts[2].GetValue(score)),
            Misses = Convert.ToInt32(Counts[3].GetValue(score)),
            Geki = Convert.ToInt32(AllCounts[3].GetValue(score)),
            Katu = Convert.ToInt32(AllCounts[4].GetValue(score)),
            Combo = Score.MaxCombo.Get(score),
            AllowPartial = true,
            WithoutRelax = mode != 3 && PerformanceOptions.WithoutRelax.Value,
            WithoutAutopilot = mode == 0 && PerformanceOptions.WithoutAutopilot.Value,
            // Only an explicitly unmodified scoring contract may supply a vanilla total.
            VanillaScoring = mode == 0 && settings.Scoring?.Adjusted != true && settings.Difficulty == null &&
                (mods & (128 | 8192 | (1 << 29))) == 0 && (!CustomRateOptions.IsEnabled(mods) || settings.Speed == RateSettings.DefaultFor(mods)),
            LegacyTotal = mode == 0 ? Convert.ToInt64(NativeStableScoring.ScoreGetter.Invoke(score, null)) : null
        };
        if (!request.VanillaScoring || request.LegacyTotal < 0) request.LegacyTotal = null;
        if (mode != 0 && request.SourceMode == 0)
        {
            try { request.ObjectStructure = NativeObjectStructure.Capture(map!, mode, mods); }
            catch (Exception e) { request.ConversionError = e.GetBaseException().Message; }
        }
        return request;
    }

    internal static void ReadOrigin(CalculationRequest request, string filename)
    {
        if (!File.Exists(filename)) return;
        using var stream = File.OpenRead(filename);
        using var reader = new BinaryReader(stream);
        var header = ReplayHeader.Read(reader);
        if (header.Version < 30000000)
        {
            var cached = OriginalScoreInfo.ReadCached(stream, header);
            if (cached != null) ApplyOrigin(request, cached);
            return;
        }
        request.StableEstimate = true;
        if (stream.Length - stream.Position < 4) return;
        int length = reader.ReadInt32();
        if (length < 13 || length > 1024 * 1024 || length > stream.Length - stream.Position) return;
        ApplyOrigin(request, JObject.Parse(ReplayRateMetadata.ReadLazerBlockJson(reader.ReadBytes(length))));
    }

    internal static void ApplyOrigin(CalculationRequest request, JObject metadata)
    {
        request.NativeLazer = false;
        bool patcher = metadata["osu_patcher"] != null;
        var classic = (metadata["mods"] as JArray)?.OfType<JObject>().SingleOrDefault(m => (string?)m["acronym"] == "CL");
        request.ClassicNoSliderHeadAccuracy = null;
        request.StableEstimate = !patcher;
        request.VanillaScoring = false; request.LegacyTotal = null;
        if (patcher) return;
        if (request.Mode != 0)
        {
            ApplyOtherOrigin(request, metadata, classic != null);
            return;
        }
        var supported = new[] { "NM", "NF", "EZ", "TD", "HD", "HR", "SD", "DT", "NC", "HT", "FL", "RX", "AP", "SO", "AT", "PF", "SV2", "CL", "DA", "MR" };
        if ((metadata["mods"] as JArray)?.OfType<JObject>().Any(m => !supported.Contains((string?)m["acronym"])) != false) return;
        var stats = metadata["statistics"] as JObject;
        var maximum = metadata["maximum_statistics"] as JObject;
        if (stats == null || maximum == null || !maximum.HasValues || maximum["great"] == null) return;
        int Read(JObject o, string key) => (int?)o[key] ?? 0;
        // Omitted zero results are normal, but maximum counts must identify a complete play.
        int great = Read(stats, "great"), ok = Read(stats, "ok"), meh = Read(stats, "meh"), miss = Read(stats, "miss");
        if (great + ok + meh + miss != Read(maximum, "great") || new[] { great, ok, meh, miss }.Any(n => n < 0)) return;
        int tick = Read(stats, "large_tick_hit"), tickMiss = Read(stats, "large_tick_miss"), tail = Read(stats, "slider_tail_hit");
        if (tick < 0 || tickMiss < 0 || tail < 0 || tick + tickMiss != Read(maximum, "large_tick_hit") || tail > Read(maximum, "slider_tail_hit")) return;
        int small = Read(stats, "small_tick_hit"), smallMiss = Read(stats, "small_tick_miss");
        if (small < 0 || smallMiss < 0 || small + smallMiss != Read(maximum, "small_tick_hit")) return;
        request.NativeLazer = true; request.StableEstimate = false;
        request.ClassicNoSliderHeadAccuracy = classic == null ? null : (bool?)classic["settings"]?["no_slider_head_accuracy"] ?? true;
        request.N300 = great; request.N100 = ok; request.N50 = meh; request.Misses = miss;
        request.TickHits = tick; request.TickMisses = tickMiss; request.TailHits = tail;
        request.SmallTickHits = small; request.SmallTickMisses = smallMiss;
    }

    private static void ApplyOtherOrigin(CalculationRequest request, JObject metadata, bool classic)
    {
        if (metadata["statistics"] is not JObject stats || metadata["maximum_statistics"] is not JObject maximum || !maximum.HasValues) return;
        if (stats.Properties().Concat(maximum.Properties()).Any(p => p.Value.Type != JTokenType.Integer || (long)p.Value < 0 || (long)p.Value > int.MaxValue)) return;
        int N(JObject o, string key) => (int?)o[key] ?? 0;
        long hits = request.Mode switch
        {
            3 => new[] { "perfect", "great", "good", "ok", "meh", "miss" }.Sum(k => (long)N(stats, k)),
            1 => new[] { "great", "ok", "miss" }.Sum(k => (long)N(stats, k)),
            _ => new[] { "great", "large_tick_hit", "miss", "large_tick_miss" }.Sum(k => (long)N(stats, k))
        };
        long expected = request.Mode == 3 ? N(maximum, "perfect") : N(maximum, "great") + (request.Mode == 2 ? (long)N(maximum, "large_tick_hit") : 0);
        if (hits != expected) return;
        if (request.Mode == 2 && N(stats, "small_tick_hit") + (long)N(stats, "small_tick_miss") != N(maximum, "small_tick_hit")) return;
        request.Statistics = stats.Properties().ToDictionary(p => p.Name, p => (int)p.Value);
        request.MaximumStatistics = maximum.Properties().ToDictionary(p => p.Name, p => (int)p.Value);
        request.Classic = classic;
        request.NativeLazer = true;
        request.StableEstimate = false;
    }

    internal static string Number(double pp) => pp.ToString("F" + PerformanceOptions.Precision, CultureInfo.InvariantCulture);
    internal static string Short(CalculationResult result) => result.Error != null ? "pp unavailable" :
        Number(result.Pp) + "pp" + (result.Semantics.StartsWith("stable estimate", StringComparison.Ordinal) ? " (stable estimate)" : "");
    internal static string Details(CalculationResult result) => result.Error != null ? "PP unavailable: " + result.Error :
        Short(result) + "\n" + result.Semantics + "; " + result.MissMethod + "; formula " + result.Version;
}
