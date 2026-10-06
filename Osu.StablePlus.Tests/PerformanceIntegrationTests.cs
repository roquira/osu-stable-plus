using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Hook.Patches.LivePerformance;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Performance;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.Graphics;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Stubs.Graphics.Skinning;

namespace Osu.StablePlus.Tests;

public partial class StableIntegrationTests
{
    [Test]
    public void GlobalPreferencesSaveOnBindingChangesWithoutRateEdits()
    {
        var options = Osu.StablePlus.Hook.Patches.PatchOptions.CreateAllPatchOptions().ToArray();
        var previous = new Settings(); foreach (var option in options) option.Save(previous);
        var directory = Path.GetFullPath(Path.Combine(".scratch", "settings-test-" + Guid.NewGuid().ToString("N")));
        var queue = new System.Collections.Generic.Queue<Action>();
        try
        {
            foreach (var option in options) option.Load(new Settings
            {
                ShowPerformanceInGame = false,
                ShowPerformanceOnLeaderboard = false,
                ShowRelaxAsRx = false,
                ApplyRelaxStarReduction = true,
                GameplayModIconOpacity = 0
            });
            using (var persistence = new SettingsPersistence(options, directory, queue.Enqueue))
            {
                PerformanceOptions.ShowPerformanceInGame.Value = true;
                PerformanceOptions.ShowPerformanceOnLeaderboard.Value = true;
                PerformanceOptions.WithoutRelax.Value = true;
                PerformanceOptions.WithoutAutopilot.Value = true;
                PerformanceOptions.DecimalPlaces.Value = 2;
                ModLabelOptions.RelaxAsRx.Value = true;
                StarRatingOptions.ReduceRelax.Value = false;
                Osu.StablePlus.Hook.Patches.UI.GameplayModIconOptions.Percent = 20;
                Assert.That(queue.Count, Is.EqualTo(1), "Coalesce changes until the next game update.");
                queue.Dequeue()();
                var saved = Settings.ReadFromDisk(directory);
                foreach (var option in options) option.Load(new Settings());
                foreach (var option in options) option.Load(saved);
                Assert.That(PerformanceOptions.ShowPerformanceInGame.Value && PerformanceOptions.ShowPerformanceOnLeaderboard.Value, Is.True);
                Assert.That(PerformanceOptions.WithoutRelax.Value && PerformanceOptions.WithoutAutopilot.Value, Is.True);
                Assert.That(PerformanceOptions.Precision, Is.EqualTo(2));
                Assert.That(ModLabelOptions.RelaxAsRx.Value, Is.True);
                Assert.That(StarRatingOptions.ReduceRelax.Value, Is.False);
                Assert.That(Osu.StablePlus.Hook.Patches.UI.GameplayModIconOptions.Percent, Is.EqualTo(20));
                // A disabled setting must survive just as an enabled one does.
                PerformanceOptions.WithoutRelax.Value = false;
                while (queue.Count > 0) queue.Dequeue()();
                Assert.That(Settings.ReadFromDisk(directory).EstimatePpWithoutRelax, Is.False);
                PerformanceOptions.DecimalPlaces.Value = 1;
                Assert.That(queue.Count, Is.EqualTo(1), "Precision saves without changing a checkbox.");
                queue.Dequeue()();
                Assert.That(Settings.ReadFromDisk(directory).PerformanceDecimalPlaces, Is.EqualTo(1));
                Osu.StablePlus.Hook.Patches.UI.GameplayModIconOptions.Percent = 40;
                Assert.That(queue.Count, Is.EqualTo(1), "Native numeric sliders also trigger saving.");
                queue.Dequeue()();
                Assert.That(Settings.ReadFromDisk(directory).GameplayModIconOpacity, Is.EqualTo(40));
            }
            PerformanceOptions.WithoutRelax.Value = true;
            Assert.That(queue.Count, Is.Zero, "Disposed subscriptions must detach.");
        }
        finally { foreach (var option in options) option.Load(previous); }
    }

    [Test]
    public void OfficialPpCountsMatchNativeReplayHeader()
    {
        string? replayPath = Environment.GetEnvironmentVariable("OSU_TEST_REPLAY");

        if (string.IsNullOrEmpty(replayPath))
            Assert.Ignore("Set OSU_TEST_REPLAY to a replay fixture to compare native and official pp.");

        using var stream = File.OpenRead(replayPath!);
        var header = ReplayHeader.Read(new BinaryReader(stream));
        stream.Position = 0;
        var score = ReadNativeReplay(stream);
        Assert.That(PerformanceRequests.Counts.Length, Is.EqualTo(4));
        var actual = PerformanceRequests.Counts.Select(f => Convert.ToInt32(f.GetValue(score))).ToArray();
        Assert.That(actual, Is.EqualTo(new[] { (int)header.Counts[0], header.Counts[1], header.Counts[2], header.Counts[5] }));
        Assert.That(LocalScorePerformance.TextField.FieldType, Is.EqualTo(typeof(string)));
    }

    private static object ReadNativeReplay(Stream stream)
    {
        using var reader = (BinaryReader)Activator.CreateInstance(Score.ReadReplay.Reference.GetParameters()[0].ParameterType, stream)!;
        var mode = reader.ReadByte();
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.ScoreMode.FieldType, mode));
        Score.ReadReplay.Invoke(score, [reader, false]);
        return score;
    }

    [Test]
    public void NativePpTextAnchorFieldsAreUnambiguous()
    {
        var parameters = pSpriteText.Constructor.Reference.GetParameters();
        foreach (int p in new[] { 3, 4 })
            Assert.That(AddPerformanceToUi.AnchorField(p).FieldType, Is.EqualTo(parameters[p].ParameterType));
    }

    [Test]
    public void LocalPpHooksKeepNativeRowTextAndTooltipLifecycle()
    {
        var instructions = LocalScoreRateDisplay.Transpiler(PatchProcessor.GetOriginalInstructions(LocalScoreRateDisplay.Target())).ToArray();
        Assert.That(instructions.Count(i => i.Calls(AccessTools.Method(typeof(LocalScorePerformance), nameof(LocalScorePerformance.AttachText)))), Is.GreaterThan(0));
        Assert.That(instructions.Count(i => i.Calls(AccessTools.Method(typeof(LocalScorePerformance), nameof(LocalScorePerformance.AssignTooltip)))), Is.GreaterThan(0));
        var scoreLine = Array.FindIndex(instructions, i => i.Calls(AccessTools.Method(typeof(LocalScorePerformance), nameof(LocalScorePerformance.ScoreLine))));
        Assert.That(scoreLine, Is.GreaterThan(1));
        Assert.That(instructions.Count(i => i.Calls(AccessTools.Method(typeof(LocalScorePerformance), nameof(LocalScorePerformance.ScoreLine)))), Is.EqualTo(1));
        Assert.That(instructions[scoreLine - 2].operand, Is.InstanceOf<MethodInfo>());
        var formatter = (MethodInfo)instructions[scoreLine - 2].operand;
        Assert.That(formatter.DeclaringType, Is.EqualTo(typeof(string)));
        Assert.That(formatter.Name, Is.EqualTo(nameof(string.Format)));
        Assert.That(instructions.Count(i => i.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 &&
            Equals(i.operand, LocalScoreRateDisplay.ScoreModLabelWidth)), Is.EqualTo(1));
    }

    [Test]
    public void PpPrecisionDropdownAndSkinFontUseNativeTypes()
    {
        var parameters = PerformanceOptions.PrecisionDropdown.Constructor.Reference.GetParameters();
        Assert.That(parameters.Length, Is.EqualTo(4));
        Assert.That(parameters[1].ParameterType.GetGenericArguments()[0], Is.EqualTo(pDropdownItem.Class.Reference));
        Assert.That(parameters[2].ParameterType.IsInstanceOfType(PerformanceOptions.DecimalPlaces.Bindable), Is.True);
        Assert.That(parameters[3].ParameterType, Is.EqualTo(typeof(EventHandler)));
        Assert.That(pDropdownItem.Constructor.Reference.GetParameters().Select(p => p.ParameterType),
            Is.EqualTo(new[] { typeof(string), typeof(object) }));
        Assert.That(pSpriteText.Constructor.Reference.GetParameters().Length, Is.EqualTo(12));
        Assert.That(SkinOsu.FontScore.Reference.FieldType, Is.EqualTo(typeof(string)));
        Assert.That(SkinOsu.FontScoreOverlap.Reference.FieldType, Is.EqualTo(typeof(int)));
        Assert.That(pSpriteText.TextConstantSpacing.Reference.FieldType, Is.EqualTo(typeof(bool)));
        Assert.That(pSpriteText.MeasureText.Reference, Is.Not.Null);
    }

    [TestCase(0, "106pp")]
    [TestCase(1, "106.4pp")]
    [TestCase(2, "106.42pp")]
    [TestCase(-1, "106pp")]
    [TestCase(99, "106.42pp")]
    public void PpPrecisionPersistsAndEstimatesHaveNoHypotheticalSuffix(int precision, string expected)
    {
        var options = new PerformanceOptions(); var previous = new Settings(); options.Save(previous);
        try
        {
            options.Load(new Settings { PerformanceDecimalPlaces = precision });
            var result = new CalculationResult { Pp = 106.423, Hypothetical = true, Semantics = "classic" };
            Assert.That(PerformanceRequests.Short(result), Is.EqualTo(expected));
            result.Semantics = "stable estimate (incomplete import)";
            Assert.That(PerformanceRequests.Short(result), Is.EqualTo(expected + " (stable estimate)"));
            var saved = new Settings(); options.Save(saved);
            var serializer = new XmlSerializer(typeof(Settings));
            using var writer = new StringWriter(); serializer.Serialize(writer, saved);
            using var reader = new StringReader(writer.ToString());
            Assert.That(((Settings)serializer.Deserialize(reader)).PerformanceDecimalPlaces, Is.EqualTo(Math.Max(0, Math.Min(2, precision))));
        }
        finally { options.Load(previous); }
    }

    [Test]
    public void OldPpPreferenceDoesNotDiscardUnrelatedSettings()
    {
        var serializer = new XmlSerializer(typeof(Settings));
        using var reader = new StringReader("<Settings><PerformanceCalculator>AkatsukiLimited</PerformanceCalculator><HalfTimeSpeed>0.6</HalfTimeSpeed><ShowRelaxAsRx>true</ShowRelaxAsRx></Settings>");
        var settings = (Settings)serializer.Deserialize(reader);
        Assert.That(settings.HalfTimeSpeed, Is.EqualTo(.6)); Assert.That(settings.ShowRelaxAsRx, Is.True);
        Assert.That(settings.EstimatePpWithoutRelax && settings.EstimatePpWithoutAutopilot, Is.True);
    }

    [Test]
    public void LazerOriginNeedsCompleteStatisticsAndPreservesClassicExports()
    {
        var request = new CalculationRequest { VanillaScoring = true, LegacyTotal = 5000 };
        PerformanceRequests.ApplyOrigin(request, JObject.Parse("{ 'mods':[], 'statistics':{'great':4}, 'maximum_statistics':{'great':5,'large_tick_hit':2,'slider_tail_hit':1}}"));
        Assert.That(request.NativeLazer, Is.False); Assert.That(request.StableEstimate, Is.True);
        PerformanceRequests.ApplyOrigin(request, JObject.Parse("{ 'mods':[], 'statistics':{'great':4,'ok':1,'large_tick_hit':1,'large_tick_miss':1,'slider_tail_hit':1}, 'maximum_statistics':{'great':5,'large_tick_hit':2,'slider_tail_hit':1}}"));
        Assert.That(request.NativeLazer, Is.True); Assert.That(request.TickMisses, Is.EqualTo(1)); Assert.That(request.N100, Is.EqualTo(1));
        Assert.That(request.LegacyTotal, Is.Null);
        request = new CalculationRequest();
        PerformanceRequests.ApplyOrigin(request, JObject.Parse("{'mods':[{'acronym':'CL'}],'osu_patcher':{},'statistics':{},'maximum_statistics':{}}"));
        Assert.That(request.NativeLazer || request.StableEstimate, Is.False);
        PerformanceRequests.ApplyOrigin(request, JObject.Parse("{'mods':[{'acronym':'CL'}],'statistics':{'great':5,'large_tick_hit':3,'small_tick_hit':1},'maximum_statistics':{'great':5,'large_tick_hit':3,'small_tick_hit':1}}"));
        Assert.That(request.NativeLazer, Is.True); Assert.That(request.ClassicNoSliderHeadAccuracy, Is.True);
        Assert.That(request.SmallTickHits, Is.EqualTo(1));
    }

    [Test]
    public void TouchDeviceOriginsRetainCompleteLazerStatistics()
    {
        var request = new CalculationRequest { Mods = 4 };
        PerformanceRequests.ApplyOrigin(request, JObject.Parse("{\"mods\":[{\"acronym\":\"TD\"}],\"statistics\":{\"great\":5,\"large_tick_hit\":3,\"slider_tail_hit\":1},\"maximum_statistics\":{\"great\":5,\"large_tick_hit\":3,\"slider_tail_hit\":1}}"));
        Assert.That(request.NativeLazer, Is.True);
        Assert.That(request.StableEstimate, Is.False);
        Assert.That(request.TickHits, Is.EqualTo(3));
        Assert.That(request.TailHits, Is.EqualTo(1));
    }

    [Test]
    public async Task HelperHandlesUnicodeCancellationAndProcessRestart()
    {
        string original = Environment.GetEnvironmentVariable("OSU_TEST_MAP")!;
        if (!File.Exists(original)) Assert.Ignore("Set OSU_TEST_MAP.");
        string copy = Path.GetFullPath(".scratch/日本語-calculator.osu");
        File.Copy(original, copy, true);
        var r = new CalculationRequest { MapPath = copy, StarsOnly = true, Rate = 1 };
        var before = await PerformanceClient.CalculateAsync(r);
        Assert.That(before.Error, Is.Null); Assert.That(before.Stars, Is.GreaterThan(0));
        Assert.ThrowsAsync<TaskCanceledException>(async () => await PerformanceClient.CalculateAsync(r, new CancellationToken(true)));
        var worker = (Process)typeof(PerformanceClient).GetField("worker", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        worker.Kill(); worker.WaitForExit(); // only the helper started by this test process
        var after = await PerformanceClient.CalculateAsync(r);
        Assert.That(after.Error, Is.Null); Assert.That(after.Stars, Is.EqualTo(before.Stars));
        Assert.That(after.Id, Is.GreaterThan(before.Id));
    }

    [Test]
    public void ImportPreservesOriginalMetadataWithoutChangingSource()
    {
        string source = Environment.GetEnvironmentVariable("OSU_TEST_DA_REPLAY")!;
        if (!File.Exists(source)) Assert.Ignore("Set OSU_TEST_DA_REPLAY.");
        var before = File.ReadAllBytes(source);
        using var stream = new MemoryStream(before, false);
        var score = ReadNativeReplay(stream);
        var json = OriginalScoreInfo.Snapshot(score);
        Assert.That(json, Is.Not.Null);
        Assert.That(JObject.Parse(json!)["statistics"], Is.Not.Null);
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(before));
    }

    [Test]
    public void PpSnapshotsIgnoreMenuAndStarPreferencesAndRejectCustomTotals()
    {
        using var source = OpenUserReplay();
        var score = ReadScore(source);
        Score.Beatmap.Set(score, CreateSelectionTestMap());
        var stars = new StarRatingOptions(); var starPrevious = new Settings(); stars.Save(starPrevious);
        var pp = new PerformanceOptions(); var ppPrevious = new Settings(); pp.Save(ppPrevious);
        var rates = new CustomRateOptions(); var ratePrevious = new Settings(); rates.Save(ratePrevious);
        try
        {
            pp.Load(new Settings { EstimatePpWithoutRelax = false, EstimatePpWithoutAutopilot = false });
            stars.Load(new Settings { ApplyRelaxStarReduction = false, ApplyAutopilotStarReduction = false });
            var before = PerformanceRequests.Capture(score);
            stars.Load(new Settings { ApplyRelaxStarReduction = true, ApplyAutopilotStarReduction = true });
            rates.Load(new Settings { DoubleTimeSpeed = 1.01 });
            var after = PerformanceRequests.Capture(score);
            Assert.That(after.Mods, Is.EqualTo(before.Mods)); Assert.That(after.Rate, Is.EqualTo(1.7));
            Assert.That(after.WithoutRelax || after.WithoutAutopilot, Is.False);
            Assert.That(after.VanillaScoring, Is.False);
            Assert.That(after.LegacyTotal, Is.Null);
            pp.Load(new Settings { EstimatePpWithoutRelax = true, EstimatePpWithoutAutopilot = true });
            after = PerformanceRequests.Capture(score);
            Assert.That(after.WithoutRelax && after.WithoutAutopilot, Is.True);
            Assert.That(after.N300, Is.EqualTo(before.N300)); Assert.That(after.Misses, Is.EqualTo(before.Misses));
        }
        finally { stars.Load(starPrevious); pp.Load(ppPrevious); rates.Load(ratePrevious); }
    }

    [TestCase("OSU_TEST_REPLAY")]
    [TestCase("OSU_TEST_DA_REPLAY")]
    public async Task RealLazerImportsCalculateWithRecordedSettings(string variable)
    {
        string path = Environment.GetEnvironmentVariable(variable)!;
        string testMap = Environment.GetEnvironmentVariable("OSU_TEST_MAP")!;
        if (!File.Exists(path) || !File.Exists(testMap)) Assert.Ignore("Set replay and map fixtures.");
        using var stream = File.OpenRead(path);
        var header = ReplayHeader.Read(new BinaryReader(stream)); stream.Position = 0;
        var score = ReadNativeReplay(stream);
        string? matching = Directory.GetFiles(Path.GetDirectoryName(testMap)!, "*.osu").FirstOrDefault(file =>
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").Equals(header.MapHash, StringComparison.OrdinalIgnoreCase);
        });
        if (matching == null) Assert.Ignore("The replay's matching difficulty is not in the test map's set.");
        Score.Beatmap.Set(score, CreateSelectionTestMap(matching));
        var request = PerformanceRequests.Capture(score);
        PerformanceRequests.ReadOrigin(request, path);
        var result = await PerformanceClient.CalculateAsync(request);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Stars, Is.GreaterThan(0));
        Assert.That(double.IsNaN(result.Pp) || double.IsInfinity(result.Pp), Is.False);
        TestContext.WriteLine(variable + ": " + request.Rate + "x, " + PerformanceRequests.Details(result));
    }
}
