using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.LivePerformance;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;

namespace Osu.StablePlus.Tests;

public partial class StableIntegrationTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void NativeLazerImportPreservesMirrorCombinationsAndTotals(int axes)
    {
        foreach (var flags in new[] { 0, 64, 576, 256 })
            foreach (var da in new[] { false, true })
            {
                var expected = new RateSettings(flags == 256 ? 0.6 : flags == 0 ? 1.5 : 1.1, flags == 576,
                    da ? new DifficultySettings(null, 3.2, 9.5, null) : null, (MirrorAxes)axes);
                var metadata = new JObject
                {
                    ["mods"] = LazerReplayExport.CreateMods(flags | MirrorSettings.Flag | 8 | 128, expected),
                    ["osu_patcher"] = new JObject()
                }; // Synthetic fixture: no real-origin cache writes.
                   // Modern lazer has no legacy standard-MR bit. Its JSON is authoritative.
                using var source = SyntheticLazerReplay(flags | 8 | 128, metadata, 713693);
                var score = ReadScore(source);
                var actual = RateControl.ForScore(score);
                Assert.That(actual.Mirror, Is.EqualTo(expected.Mirror));
                Assert.That(actual.Speed, Is.EqualTo(expected.Speed));
                Assert.That(actual.AdjustPitch, Is.EqualTo(expected.AdjustPitch));
                Assert.That(actual.Difficulty?.Values, Is.EqualTo(expected.Difficulty?.Values));
                Assert.That(actual.Scoring, Is.Null);
                Assert.That(RateControl.GetMods(score) & MirrorSettings.Flag, Is.Not.Zero);
                Assert.That(RulesetCompatibility.UnsupportedReplay(score), Is.False);
                Assert.That(LocalScoreRateDisplay.Format("HD,DT,RX", score), Does.Contain("MR("));
                Assert.That(Convert.ToInt32(NativeStableScoring.ScoreGetter.Invoke(score, null)), Is.EqualTo(713693));

                // Stable rewrites an import: settings and total must survive a restart.
                using var saved = new MemoryStream();
                using (var writer = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, saved)!)
                    Score.WriteReplay.Invoke(score, [writer]);
                using var copy = new MemoryStream(saved.ToArray());
                var reloaded = ReadScore(copy);
                Assert.That(RateControl.ForScore(reloaded).Mirror, Is.EqualTo(expected.Mirror));
                Assert.That(RateControl.ForScore(reloaded).Speed, Is.EqualTo(expected.Speed));
                Assert.That(RateControl.ForScore(reloaded).Difficulty?.Values, Is.EqualTo(expected.Difficulty?.Values));
                Assert.That(Score.ReplayData.Get(reloaded), Is.EqualTo(Score.ReplayData.Get(score)));
                Assert.That(Convert.ToInt32(NativeStableScoring.ScoreGetter.Invoke(reloaded, null)), Is.EqualTo(713693));
            }
    }

    [TestCase("{\"mods\":[{\"acronym\":\"DT\"},{\"acronym\":\"WU\"}]}")]
    [TestCase("{\"mods\":[{\"acronym\":\"DT\"},{\"acronym\":\"MR\",\"settings\":{\"reflection\":3}}]}")]
    [TestCase("{\"mods\":[{\"acronym\":\"DT\"},{\"acronym\":\"DA\",\"settings\":{\"approach_rate\":11}}]}")]
    public void UnsupportedOrMalformedLazerSettingsRemainFlaggedAfterNativeSave(string json)
    {
        using var source = SyntheticLazerReplay(64, JObject.Parse(json), 1041199);
        var score = ReadScore(source);
        Assert.That(RulesetCompatibility.UnsupportedReplay(score), Is.True);
        Assert.That(LocalScoreRateDisplay.Format("DT", score), Does.Contain("unsupported lazer settings"));
        using var saved = new MemoryStream();
        using (var writer = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, saved)!)
            Score.WriteReplay.Invoke(score, [writer]);
        using var copy = new MemoryStream(saved.ToArray());
        Assert.That(RulesetCompatibility.UnsupportedReplay(ReadScore(copy)), Is.True);
    }

    [Test]
    public void SuppliedDtMirrorReplayImportsWithoutChangingSourceOrFrames()
    {
        var path = Environment.GetEnvironmentVariable("OSU_TEST_MR_REPLAY");
        if (!File.Exists(path)) Assert.Ignore("Set OSU_TEST_MR_REPLAY to the supplied DT1.1 + MR(Both) replay.");
        var bytes = File.ReadAllBytes(path!);
        using var headerStream = new MemoryStream(bytes, false);
        var header = ReplayHeader.Read(new BinaryReader(headerStream));
        using var source = new MemoryStream(bytes, false);
        var score = ReadScore(source);
        Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(1.1));
        Assert.That(RateControl.ForScore(score).Mirror, Is.EqualTo(MirrorAxes.Both));
        Assert.That(RulesetCompatibility.UnsupportedReplay(score), Is.False);
        Assert.That(LocalScoreRateDisplay.Format("DT", score), Is.EqualTo("DT(1.1x),MR(B)"));
        Assert.That(Convert.ToInt32(NativeStableScoring.ScoreGetter.Invoke(score, null)), Is.EqualTo(header.Total));
        Assert.That(Score.ReplayData.Get(score), Is.EqualTo(bytes.Skip(header.FramesOffset).Take(header.FramesLength).ToArray()));
        Assert.That(File.ReadAllBytes(path!), Is.EqualTo(bytes));
    }

    [TestCase("OSU_TEST_SCORING_LAZER_REPLAY", 1041199, 1.3)]
    [TestCase("OSU_TEST_SCORING_STABLE_REPLAY", 71215303, 1.5)]
    public void SuppliedChocolateScrambleReplaysKeepTheirOriginalScoreScale(string variable, int total, double rate)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (!File.Exists(path)) Assert.Ignore("Set " + variable + " to the supplied Chocolate Scramble replay.");
        var bytes = File.ReadAllBytes(path!);
        using var source = new MemoryStream(bytes, false);
        var score = ReadScore(source);
        Assert.That(Convert.ToInt32(NativeStableScoring.ScoreGetter.Invoke(score, null)), Is.EqualTo(total));
        Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(rate));
        Assert.That(RateControl.ForScore(score).Scoring, Is.Null, "No custom stable-scoring contract is added on import.");
        Assert.That(File.ReadAllBytes(path!), Is.EqualTo(bytes));
    }

    [TestCase("{\"mods\":[{\"acronym\":\"HD\"}]}", false)]
    [TestCase("{\"mods\":[{\"acronym\":\"MR\",\"settings\":{\"reflection\":2}}]}", false)]
    [TestCase("{\"mods\":[{\"acronym\":\"MR\",\"settings\":{\"reflection\":3}}]}", true)]
    [TestCase("{\"mods\":[{\"acronym\":\"DC\"}]}", true)]
    public void LazyReplayFileLookupDistinguishesNativeDefaultsFromUnsupportedMetadata(string json, bool unsupported)
    {
        using var replay = SyntheticLazerReplay(8, JObject.Parse(json), 713693);
        var filename = Path.GetFullPath(Path.Combine(".scratch", "lazer-lookup-" + Guid.NewGuid().ToString("N") + ".osr"));
        File.WriteAllBytes(filename, replay.ToArray());
        try
        {
            var settings = (RateSettings)typeof(RateControl).GetMethod("ReadFile", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, [filename, 8]);
            Assert.That(settings.UnsupportedRulesetSettings, Is.EqualTo(unsupported));
        }
        finally { File.Delete(filename); }
    }

    [Test]
    public void OldFailedImportsRecoverOriginalSettingsFromFrameKeyedCacheWithoutRewritingReplay()
    {
        var previousDirectory = OriginalScoreInfo.DirectoryPath;
        OriginalScoreInfo.DirectoryPath = Path.GetFullPath(Path.Combine(".scratch", "origin-recovery-" + Guid.NewGuid().ToString("N")));
        try
        {
            var original = JObject.Parse("{\"mods\":[{\"acronym\":\"DT\",\"settings\":{\"speed_change\":1.1}},{\"acronym\":\"MR\",\"settings\":{\"reflection\":2}}]}");
            using var replay = SyntheticLazerReplay(64, new JObject { ["mods"] = new JArray(new JObject { ["acronym"] = "DT" }), ["osu_patcher"] = new JObject() }, 713693);
            var oldImport = ReadScore(replay); // Models the former fallback: DT1.5, no MR.
            using var saved = new MemoryStream();
            using (var writer = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, saved)!)
                Score.WriteReplay.Invoke(oldImport, [writer]);
            var bytes = saved.ToArray();
            Directory.CreateDirectory(OriginalScoreInfo.DirectoryPath);
            File.WriteAllText(Path.Combine(OriginalScoreInfo.DirectoryPath, ReplayHeader.Hash(Score.ReplayData.Get(oldImport)!) + ".json"), original.ToString());

            using var source = new MemoryStream(bytes, false);
            var repaired = ReadScore(source);
            Assert.That(RateControl.ForScore(repaired).Speed, Is.EqualTo(1.1));
            Assert.That(RateControl.ForScore(repaired).Mirror, Is.EqualTo(MirrorAxes.Both));
            Assert.That(RateControl.GetMods(repaired) & MirrorSettings.Flag, Is.Not.Zero);
            Assert.That(Convert.ToInt32(NativeStableScoring.ScoreGetter.Invoke(repaired, null)), Is.EqualTo(713693));
            Assert.That(source.ToArray(), Is.EqualTo(bytes));

            using var lookup = new MemoryStream(bytes, false);
            lookup.Position = 12;
            Assert.That(OriginalScoreInfo.RestoreReplaySettings(lookup, 64, 0)!.Mirror, Is.EqualTo(MirrorAxes.Both));
            Assert.That(lookup.Position, Is.EqualTo(12));
        }
        finally { OriginalScoreInfo.DirectoryPath = previousDirectory; }
    }

    private static MemoryStream SyntheticLazerReplay(int flags, JObject metadata, int total)
    {
        metadata = (JObject)metadata.DeepClone();
        metadata["osu_patcher"] ??= new JObject(); // Do not populate the real-import origin cache with synthetic data.
        var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        void Text(string value) { writer.Write((byte)11); writer.Write(value); }
        writer.Write((byte)0); writer.Write(LazerReplayExport.ContainerVersion);
        Text(new string('a', 32)); Text("fixture"); Text(new string('b', 32));
        foreach (ushort count in new ushort[] { 50, 2, 1, 0, 0, 3 }) writer.Write(count);
        writer.Write(total); writer.Write((ushort)20); writer.Write(false); writer.Write(flags);
        Text(""); writer.Write(DateTime.UtcNow.Ticks);
        var frames = Guid.NewGuid().ToByteArray(); // Unique identity for each synthetic replay's cache key.
        writer.Write(frames.Length); writer.Write(frames); writer.Write(0L);
        var compressed = LazerReplayExport.Compress(metadata.ToString());
        writer.Write(compressed.Length); writer.Write(compressed);
        output.Position = 0;
        return output;
    }
}
