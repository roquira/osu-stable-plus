using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Decoder = SevenZip.Compression.LZMA.Decoder;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Reads lazer's LZMA score-info block and our stable replay extension.</summary>
internal static class ReplayRateMetadata
{
    // A stable replay keeps its real version/scoring semantics. Unpatched stable ignores this trailer.
    // The pre-rename "osu!patcher" prefix is part of the saved format: existing replays depend on it.
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("osu!patcher-DT\0\x01");
    private static readonly byte[] DifficultyMagic = Encoding.ASCII.GetBytes("osu!patcher-DA\0\x01");
    private static readonly byte[] MirrorMagic = Encoding.ASCII.GetBytes("osu!patcher-MR\0\x01");
    private static readonly byte[] ScoringMagic = Encoding.ASCII.GetBytes("osu!patcher-SCORE\0\x01");
    private static readonly byte[] UnsupportedMagic = Encoding.ASCII.GetBytes("osu!patcher-UNSUPPORTED\0\x01");
    internal static int StableTailLength => Magic.Length + sizeof(double) + sizeof(byte);
    private const int MaxMetadataBytes = 1024 * 1024;
    private const uint MaxDictionaryBytes = 16 * 1024 * 1024;

    internal static RateSettings? ReadReplay(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        var mode = reader.ReadByte();
        var version = reader.ReadInt32();
        SkipString(reader); // beatmap hash
        SkipString(reader); // player name
        SkipString(reader); // replay hash
        Skip(reader, 19); // hit counts, score, combo, perfect
        var mods = reader.ReadInt32();
        SkipString(reader); // HP graph
        Skip(reader, 8); // timestamp
        var framesLength = reader.ReadInt32();
        if (framesLength < 0) throw new InvalidDataException("Missing replay frames.");
        Skip(reader, framesLength);
        if (version >= 20140721) Skip(reader, 8);
        else if (version >= 20121008) Skip(reader, 4);
        // Target practice has an extra double in stable, but lazer does not export that mod.
        if (version < 30000000 && (mods & (1 << 23)) != 0) Skip(reader, 8);
        return ReadTail(stream, version, mods, mode);
    }

    internal static void Skip(BinaryReader reader, int count)
    {
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Truncated replay.");
        reader.BaseStream.Seek(count, SeekOrigin.Current);
    }

    internal static void SkipString(BinaryReader reader)
    {
        var marker = reader.ReadByte();
        if (marker == 0) return;
        if (marker != 0x0b) throw new InvalidDataException("Invalid replay string marker.");
        uint length = 0;
        for (var shift = 0; shift <= 28; shift += 7)
        {
            var part = reader.ReadByte();
            if (shift == 28 && part > 7) throw new InvalidDataException("Replay string is too long.");
            length |= (uint)(part & 127) << shift;
            if ((part & 128) != 0) continue;
            Skip(reader, checked((int)length));
            return;
        }
        throw new InvalidDataException("Invalid replay string length.");
    }

    /// <summary>Called after stable has read replay frames, online ID, and mod-specific data.</summary>
    internal static RateSettings? ReadTail(Stream stream, int version, int mods, int mode = 0)
    {
        if (!stream.CanSeek || !stream.CanRead) return null;
        var position = stream.Position;
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (version >= 30000001)
            {
                var size = reader.ReadInt32();
                if (size < 13 || size > MaxMetadataBytes || size > stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid lazer score-info length.");
                return ReadLazerBlock(reader.ReadBytes(size), mods, mode);
            }

            if (stream.Length - stream.Position < Magic.Length || !reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
                return null;
            var speed = reader.ReadDouble();
            var pitch = reader.ReadByte();
            if (!RateSettings.IsValidSpeed(speed) || pitch > 1)
                throw new InvalidDataException("Invalid osu!patcher DT settings.");
            DifficultySettings? difficulty = null;
            bool Marker(byte[] marker)
            {
                var start = stream.Position;
                if (stream.Length - start >= marker.Length && reader.ReadBytes(marker.Length).SequenceEqual(marker)) return true;
                stream.Position = start;
                return false;
            }
            if (Marker(DifficultyMagic))
            {
                var values = new double?[4];
                for (var i = 0; i < 4; i++) { var v = reader.ReadDouble(); values[i] = double.IsNaN(v) ? null : v; }
                difficulty = new DifficultySettings(values);
            }
            MirrorAxes? mirror = Marker(MirrorMagic) ? (MirrorAxes)reader.ReadByte() : null;
            StableScoringSettings? scoring = null;
            if (Marker(ScoringMagic))
            {
                try
                {
                    var scoringVersion = reader.ReadInt32();
                    var factor = reader.ReadInt32();
                    scoring = new StableScoringSettings(scoringVersion, factor == -1 ? null : factor);
                }
                catch (Exception e) when (e is ArgumentException || e is EndOfStreamException)
                {
                    Console.WriteLine("[Stable scoring] Unsupported replay scoring record; retaining legacy scoring: " + e.Message);
                    scoring = new StableScoringSettings(0);
                }
            }
            var unsupported = Marker(UnsupportedMagic);
            return new RateSettings(speed, pitch == 1, mode == 0 ? difficulty : null, mode == 0 ? mirror : null,
                scoring, unsupported || (mode != 0 && (difficulty != null || mirror != null)));
        }
        finally
        {
            // Do not consume anything belonging to stable's caller.
            stream.Position = position;
        }
    }

    internal static RateSettings? ReadLazerBlock(byte[] data, int mods, int mode = 0)
        => ReadLazerJson(ReadLazerBlockJson(data), mods, mode);

    internal static string ReadLazerBlockJson(byte[] data)
    {
        if (data.Length < 13 || data.Length > MaxMetadataBytes)
            throw new InvalidDataException("Invalid LZMA header.");
        var dictionarySize = BitConverter.ToUInt32(data, 1);
        var outputSize = BitConverter.ToInt64(data, 5);
        if (dictionarySize > MaxDictionaryBytes || outputSize < 0 || outputSize > MaxMetadataBytes)
            throw new InvalidDataException("Lazer score-info exceeds the metadata limit.");

        var decoder = new Decoder();
        decoder.SetDecoderProperties(data.Take(5).ToArray());
        using var input = new MemoryStream(data, 13, data.Length - 13);
        // A fixed buffer also bounds output for a malformed compressed stream.
        using var output = new MemoryStream(new byte[(int)outputSize], true);
        decoder.Code(input, output, input.Length, outputSize, null);
        if (output.Position != outputSize) throw new InvalidDataException("Truncated lazer score-info.");
        return Encoding.UTF8.GetString(output.ToArray());
    }

    internal static RateSettings? ReadLazerJson(string json, int mods, int mode = 0)
    {
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        var root = JObject.Load(reader);
        var scoring = ReadScoringJson(root["osu_patcher"]?["scoring"]);
        var entries = root["mods"] as JArray ?? throw new InvalidDataException("Missing lazer mods.");
        var da = entries.OfType<JObject>().SingleOrDefault(m => (string?)m["acronym"] == "DA");
        DifficultySettings? difficulty = null;
        if (da != null && mode == 0)
        {
            var options = da["settings"] as JObject;
            difficulty = new DifficultySettings(new[] { "drain_rate", "circle_size", "approach_rate", "overall_difficulty" }
                .Select(key => (double?)options?[key]).ToArray());
        }
        var rateMods = entries.OfType<JObject>().Where(m => (string?)m["acronym"] is "DT" or "NC" or "HT").ToArray();
        MirrorAxes? mirror = null;
        var mr = entries.OfType<JObject>().SingleOrDefault(m => (string?)m["acronym"] == "MR");
        // Keep the fact that settings were present even though they must not be
        // interpreted using standard's DA/axis model. Mania's native MR is fine.
        var unsupported = HasUnsupportedLazerMods(entries, mode) || mode != 0 && (da != null || (mr != null && mode != 3));
        if (mr != null && mode == 0)
        {
            var token = mr["settings"]?["reflection"];
            mirror = ReadMirror(token);
            if ((mods & 16) != 0) throw new InvalidDataException("Mirror is incompatible with HardRock.");
        }
        if (rateMods.Length == 0) return difficulty == null && mirror == null && scoring == null && !unsupported ? null :
            new RateSettings(difficulty: difficulty, mirror: mirror, scoring: scoring, unsupportedRulesetSettings: unsupported);
        if (rateMods.Length != 1 || ((mods & 64) != 0 && (mods & 256) != 0)) throw new InvalidDataException("Conflicting rate mods.");
        var mod = rateMods[0];
        var halfTime = (string?)mod["acronym"] == "HT";
        if ((mods & (halfTime ? 256 : 64)) == 0) throw new InvalidDataException("Conflicting rate mods.");
        var settings = mod["settings"] as JObject;
        var speedToken = settings?["speed_change"];
        var pitchToken = settings?["adjust_pitch"];
        if (speedToken != null && speedToken.Type != JTokenType.Float && speedToken.Type != JTokenType.Integer)
            throw new InvalidDataException("Invalid speed_change.");
        if (pitchToken != null && pitchToken.Type != JTokenType.Boolean)
            throw new InvalidDataException("Invalid adjust_pitch.");
        var speed = (double?)speedToken ?? (halfTime ? 0.75 : RateSettings.DefaultSpeed);
        if (!RateSettings.IsValidSpeed(speed, halfTime)) throw new InvalidDataException("Speed is outside the rate mod's range.");
        return new RateSettings(speed, (string?)mod["acronym"] == "NC" || ((bool?)pitchToken ?? false), difficulty, mirror, scoring, unsupported);
    }

    private static bool HasUnsupportedLazerMods(JArray entries, int mode)
    {
        var common = new[] { "NM", "NF", "EZ", "HD", "HR", "SD", "DT", "NC", "HT", "FL", "AT", "PF", "SV2", "CL" };
        var standard = new[] { "TD", "RX", "AP", "SO", "DA", "MR" };
        var mania = new[] { "1K", "2K", "3K", "4K", "5K", "6K", "7K", "8K", "9K", "DS", "FI", "MR" };
        foreach (var token in entries)
        {
            if (token is not JObject entry) return true;
            var acronym = (string?)entry["acronym"];
            if (!common.Contains(acronym) && !(mode == 0 ? standard.Contains(acronym) : mode == 3 ? mania.Contains(acronym) : acronym == "RX"))
                return true;
            if (entry["settings"] == null || entry["settings"]!.Type == JTokenType.Null) continue;
            if (entry["settings"] is not JObject values) return true;
            // A lazer-only mod, or custom HD/FL setting, must not silently become
            // an unrelated legacy replay. Mania MR only supports its native default.
            var allowed = acronym == "DT" || acronym == "HT" ? new[] { "speed_change", "adjust_pitch" } :
                acronym == "NC" ? new[] { "speed_change" } :
                mode == 0 && acronym == "DA" ? new[] { "drain_rate", "circle_size", "approach_rate", "overall_difficulty", "extended_limits" } :
                mode == 0 && acronym == "MR" ? new[] { "reflection" } : new string[0];
            if (mode == 0 && acronym == "CL")
            {
                // Explicit default Classic settings are equivalent to stable.
                var defaults = new[] { "no_slider_head_accuracy", "classic_note_lock", "always_play_tail_sample", "fade_hit_circle_early", "classic_health" };
                if (values.Properties().Any(p => !defaults.Contains(p.Name) || p.Value.Type != JTokenType.Boolean || !(bool)p.Value)) return true;
                continue;
            }
            if (values.Properties().Any(p => !allowed.Contains(p.Name))) return true;
            if (mode == 0 && acronym == "DA" && values["extended_limits"] is { Type: not JTokenType.Boolean }) return true;
        }
        return false;
    }

    private static MirrorAxes ReadMirror(JToken? token)
    {
        if (token == null) return MirrorAxes.Horizontal;
        // Modern lazer serializes MirrorType as an integer. Keep accepting names
        // emitted by earlier stable+ builds, without coercing invalid JSON types.
        if (token.Type == JTokenType.Integer && (long)token >= 0 && (long)token <= 2)
            return (MirrorAxes)(int)token;
        if (token.Type == JTokenType.String)
            switch (((string)token!).ToLowerInvariant())
            {
                case "horizontal": return MirrorAxes.Horizontal;
                case "vertical": return MirrorAxes.Vertical;
                case "both": return MirrorAxes.Both;
            }
        throw new InvalidDataException("Invalid Mirror reflection.");
    }

    internal static JObject ScoringJson(StableScoringSettings scoring) => new()
    {
        ["version"] = scoring.Version,
        ["difficulty_factor"] = scoring.DifficultyFactor
    };

    private static StableScoringSettings? ReadScoringJson(JToken? token)
    {
        if (token == null) return null;
        try
        {
            if (token is not JObject json) throw new ArgumentException("Invalid scoring record.");
            if (json["version"]?.Type != JTokenType.Integer ||
                (json["difficulty_factor"] is { Type: not JTokenType.Null and not JTokenType.Integer }))
                throw new ArgumentException("Invalid scoring record types.");
            return new StableScoringSettings((int)json["version"]!, (int?)json["difficulty_factor"]);
        }
        catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
        {
            Console.WriteLine("[Stable scoring] Unsupported exported scoring record; retaining legacy scoring: " + e.Message);
            return new StableScoringSettings(0);
        }
    }

    internal static void WriteTail(Stream stream, RateSettings settings)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Magic);
        writer.Write(settings.Speed);
        writer.Write(settings.AdjustPitch);
        if (settings.Difficulty != null)
        {
            writer.Write(DifficultyMagic);
            foreach (var value in settings.Difficulty.Values) writer.Write(value ?? double.NaN);
        }
        if (settings.Mirror is { } axes) { writer.Write(MirrorMagic); writer.Write((byte)axes); }
        if (settings.Scoring is { } scoring)
        {
            writer.Write(ScoringMagic);
            writer.Write(scoring.Version);
            writer.Write(scoring.DifficultyFactor ?? -1);
        }
        if (settings.UnsupportedRulesetSettings) writer.Write(UnsupportedMagic);
    }
}
