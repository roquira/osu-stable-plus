using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SevenZip;
using Encoder = SevenZip.Compression.LZMA.Encoder;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Build a lazer-readable container without changing recorded input frames.</summary>
internal static class LazerReplayExport
{
    internal const int ContainerVersion = 30000019;
    internal const int MaxReplayBytes = 128 * 1024 * 1024;

    internal static byte[] Convert(byte[] replay, RateSettings? settings = null)
    {
        if (replay.Length > MaxReplayBytes) throw new InvalidDataException("Replay is too large to export.");
        using var input = new MemoryStream(replay, false);
        using var reader = new BinaryReader(input, Encoding.UTF8, true);
        var mode = reader.ReadByte();
        var version = reader.ReadInt32();
        if (mode > 3 || version < 20140721 || version >= 30000000)
            throw new InvalidDataException("Expected a modern stable replay.");
        ReplayRateMetadata.SkipString(reader); // beatmap hash
        ReplayRateMetadata.SkipString(reader); // username
        ReplayRateMetadata.SkipString(reader); // replay hash
        var n300 = reader.ReadUInt16();
        var n100 = reader.ReadUInt16();
        var n50 = reader.ReadUInt16();
        var geki = reader.ReadUInt16();
        var katu = reader.ReadUInt16();
        var misses = reader.ReadUInt16();
        var total = reader.ReadInt32();
        reader.ReadUInt16(); // max combo
        reader.ReadBoolean();
        var modsOffset = input.Position;
        var mods = reader.ReadInt32();
        ReplayRateMetadata.SkipString(reader); // health graph
        reader.ReadInt64(); // timestamp
        ReplayRateMetadata.Skip(reader, reader.ReadInt32());
        reader.ReadInt64(); // legacy online ID
        var nativeEnd = checked((int)input.Position);
        if ((mods & (1 << 23)) != 0)
            throw new InvalidDataException("Lazer does not support stable's Target Practice replay data.");
        settings ??= ReplayRateMetadata.ReadTail(input, version, mods, mode);
        if (settings == null) throw new InvalidDataException("Replay has no saved DT rate settings.");

        var statistics = new JObject { ["great"] = n300, ["miss"] = misses };
        if (mode == 2)
        {
            statistics["large_tick_hit"] = n100;
            statistics["small_tick_hit"] = n50;
            statistics["small_tick_miss"] = katu;
        }
        else
        {
            statistics["ok"] = n100;
            if (mode != 1) statistics["meh"] = n50;
            if (mode == 1) statistics["large_bonus"] = geki + katu;
            if (mode == 3)
            {
                statistics["perfect"] = geki;
                statistics["good"] = katu;
            }
        }
        var metadata = new JObject
        {
            ["online_id"] = -1,
            ["mods"] = CreateMods(mods, settings, mode),
            ["statistics"] = statistics,
            ["maximum_statistics"] = new JObject(),
            ["client_version"] = "osu!stable " + version + " + " + Product.Name,
            // Keep the native total verbatim; do not pretend to have calculated a
            // lazer score multiplier or manufacture missing slider judgements.
            ["total_score_without_mods"] = 0,
            ["pauses"] = new JArray(),
            // Pre-rename key kept for compatibility with previously exported replays.
            ["osu_patcher"] = new JObject { ["stable_version"] = version, ["legacy_total_score"] = total }
        };
        if (settings.Scoring is { } scoring)
            metadata["osu_patcher"]!["scoring"] = ReplayRateMetadata.ScoringJson(scoring);
        var compressed = Compress(metadata.ToString(Formatting.None));
        using var output = new MemoryStream();
        output.Write(replay, 0, nativeEnd);
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        output.Position = 1;
        writer.Write(ContainerVersion);
        output.Position = modsOffset;
        writer.Write(mods & int.MaxValue);
        output.Position = nativeEnd;
        writer.Write(compressed.Length);
        writer.Write(compressed);
        return output.ToArray();
    }

    internal static JArray CreateMods(int flags, RateSettings settings, int mode = 0)
    {
        if (mode != 0 && (settings.Difficulty != null || settings.Mirror != null || (flags & int.MinValue) != 0))
            throw new InvalidDataException("DA and configurable Mirror are standard-only.");
        var result = new JArray();
        var remaining = flags & int.MaxValue;
        void Add(int mask, string acronym)
        {
            if ((remaining & mask) == 0) return;
            result.Add(new JObject { ["acronym"] = acronym });
            remaining &= ~mask;
        }
        // Composite legacy mods include their base mod bit.
        if ((flags & 512) != 0) remaining &= ~64;
        if ((flags & 16384) != 0) remaining &= ~32;
        Add(1, "NF"); Add(2, "EZ"); Add(4, "TD"); Add(8, "HD"); Add(16, "HR");
        Add(32, "SD"); Add(64, "DT"); Add(128, "RX"); Add(256, "HT"); Add(512, "NC");
        Add(1024, "FL"); Add(2048, "AT"); Add(4096, "SO"); Add(8192, "AP"); Add(16384, "PF");
        Add(1 << 15, "4K"); Add(1 << 16, "5K"); Add(1 << 17, "6K");
        Add(1 << 18, "7K"); Add(1 << 19, "8K"); Add(1 << 20, "FI");
        Add(1 << 21, "RD"); Add(1 << 24, "9K"); Add(1 << 25, "DS");
        Add(1 << 26, "1K"); Add(1 << 27, "3K"); Add(1 << 28, "2K");
        Add(1 << 29, "SV2"); Add(1 << 30, "MR");
        if (remaining != 0) throw new InvalidDataException("This replay contains mods that cannot be exported to lazer.");
        if (settings.Difficulty != null)
        {
            var values = new JObject();
            var keys = new[] { "drain_rate", "circle_size", "approach_rate", "overall_difficulty" };
            for (var i = 0; i < 4; i++)
                if (settings.Difficulty.Values[i].HasValue) values[keys[i]] = settings.Difficulty.Values[i]!.Value;
            result.Add(new JObject { ["acronym"] = "DA", ["settings"] = values });
        }
        foreach (JObject mod in result)
        {
            if ((string?)mod["acronym"] == "MR" && settings.Mirror.HasValue)
                mod["settings"] = new JObject { ["reflection"] = (int)settings.Mirror.Value };
            if ((string?)mod["acronym"] is "DT" or "NC" or "HT")
            {
                var rateSettings = new JObject { ["speed_change"] = settings.Speed };
                if ((string?)mod["acronym"] != "NC") rateSettings["adjust_pitch"] = settings.AdjustPitch;
                mod["settings"] = rateSettings;
            }
        }
        // The high container version disables lazer's automatic legacy Classic mod.
        // Add it explicitly to request stable-like gameplay for these recorded frames.
        result.Add(new JObject { ["acronym"] = "CL" });
        return result;
    }

    internal static byte[] Compress(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var input = new MemoryStream(bytes, false);
        using var output = new MemoryStream();
        var encoder = new Encoder();
        encoder.SetCoderProperties([CoderPropID.DictionarySize, CoderPropID.EndMarker], [1 << 20, false]);
        encoder.WriteCoderProperties(output);
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write((long)bytes.Length);
        encoder.Code(input, output, bytes.Length, -1, null);
        return output.ToArray();
    }
}
