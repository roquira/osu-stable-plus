using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

/// <summary>Preserve imported lazer statistics separately when stable rewrites its replay container.</summary>
internal static class OriginalScoreInfo
{
    private static readonly ConditionalWeakTable<byte[], Holder> Memory = new();
    internal static string DirectoryPath = Path.Combine(Path.GetDirectoryName(typeof(OriginalScoreInfo).Assembly.Location)!,
        "performance-cache", "origins");

    internal static void Capture(object score, Stream stream, int version)
    {
        if (version < 30000001 || !stream.CanSeek || Score.ReplayData.Get(score) is not { } frames) return;
        long position = stream.Position;
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            int length = reader.ReadInt32();
            if (length < 13 || length > 1024 * 1024 || length > stream.Length - stream.Position) return;
            var json = ReplayRateMetadata.ReadLazerBlockJson(reader.ReadBytes(length));
            if (JObject.Parse(json)["osu_patcher"] != null) return;
            Memory.Remove(frames); Memory.Add(frames, new Holder(json));
            // The source replay/database is never changed. Cache I/O stays off the update thread.
            _ = Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.WriteAllText(Path.Combine(DirectoryPath, ReplayHeader.Hash(frames) + ".json"), json, new UTF8Encoding(false));
                }
                catch (Exception e) { Console.WriteLine("[Original score cache] " + e.Message); }
            });
        }
        catch (Exception e) { Console.WriteLine("[Original score metadata] " + e.Message); }
        finally { stream.Position = position; }
    }

    internal static string? Snapshot(object score) => Score.ReplayData.Get(score) is { } frames &&
        Memory.TryGetValue(frames, out var original) ? original.Json : null;

    // Only for the requested score/replay. No library scan.
    internal static JObject? ReadCached(Stream replay, ReplayHeader header)
    {
        if (header.FramesLength <= 0 || header.FramesLength > LazerReplayExport.MaxReplayBytes) return null;
        replay.Position = header.FramesOffset;
        using var reader = new BinaryReader(replay, Encoding.UTF8, true);
        string path = Path.Combine(DirectoryPath, ReplayHeader.Hash(reader.ReadBytes(header.FramesLength)) + ".json");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return null;
        return JObject.Parse(File.ReadAllText(path));
    }

    // Earlier imports may have saved default DT and dropped MR after a parser
    // failure. The untouched original JSON, keyed by recorded frames, can repair
    // their in-memory settings without rewriting replays or the score database.
    internal static RateSettings? RestoreReplaySettings(Stream replay, int mods, int mode)
    {
        if (!replay.CanSeek) return null;
        var position = replay.Position;
        var originalFound = false;
        try
        {
            replay.Position = 0;
            using var reader = new BinaryReader(replay, Encoding.UTF8, true);
            var header = ReplayHeader.Read(reader);
            if (header.Version >= 30000000) return null;
            var metadata = ReadCached(replay, header);
            if (metadata == null) return null;
            originalFound = true;
            return ReplayRateMetadata.ReadLazerJson(metadata.ToString(), mods, mode);
        }
        catch (Exception e)
        {
            Console.WriteLine("[Original replay settings] " + e.Message);
            return originalFound ? new RateSettings(RateSettings.DefaultFor(mods), (mods & 512) != 0, unsupportedRulesetSettings: true) : null;
        }
        finally { replay.Position = position; }
    }

    private sealed class Holder(string json) { internal readonly string Json = json; }
}
