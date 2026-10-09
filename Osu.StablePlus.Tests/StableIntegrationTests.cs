using System;
using Osu.StablePlus.Performance;
using Osu.StablePlus.Hook.Patches.LivePerformance;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Hook.Patches.Mods.AudioPreview;
using Osu.StablePlus.Hook.Patches.Relax;
using Osu.StablePlus.Hook.Patches.UI;
using Osu.StablePlus.Stubs.Audio;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Tests;

[TestFixture, NonParallelizable]
public partial class StableIntegrationTests
{
    private Harmony harmony = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OSU_PATH")))
            Assert.Ignore("Set OSU_PATH to an installed stable directory for integration tests.");
        Osu.StablePlus.Stubs.Tests.OsuLoader.UpdateAndLoad().GetAwaiter().GetResult();
        PerformanceClient.HelperPath = Path.GetFullPath(Environment.GetEnvironmentVariable("PERFORMANCE_HELPER_PATH") ?? "Osu.StablePlus.Performance.Engine/bin/Release/net8.0/osu-stable-plus-performance.exe");
        harmony = new Harmony("osu-stable-plus.rate-tests");
        // Resolve bindings with shared decoded IL, as the hook does during installation.
        using var ilCache = IlCache.Begin();
        foreach (var type in new[] { typeof(TrackOnScoreHit), typeof(TrackResetScore), typeof(AddPerformanceToUi), typeof(AllowOpenOptionsInGameplay), typeof(AllowRelaxComboBreakSound), typeof(AllowRelaxDrawMisses),
                     typeof(AllowRelaxFailing), typeof(AllowRelaxLowHpGlow), typeof(AutoSaveRelaxScores),
                     typeof(CustomRatePlayback), typeof(ReadReplayRate), typeof(WriteReplayRate),
                     typeof(FixUpdatePlaybackRate), typeof(AddModMenuRateControl),
                     typeof(DisposeModMenuRateControl), typeof(ModSelectAudioPreview), typeof(TrackUpdatePreviewMusic),
                     typeof(CaptureGameplayRate), typeof(CopyClonedReplayRate), typeof(LocalScoreRateDisplay), typeof(SortLocalScores),
                     typeof(PreserveReplaySeekRate), typeof(CustomRateTiming), typeof(ExportLazerReplay),
                     typeof(AllowNoFailAssistance), typeof(SelectedRateDisplay), typeof(RestoreModMenuRateVisibility),
                     typeof(ApplyDifficultyAdjust), typeof(DifficultyDrainScope), typeof(ResetDifficultyMod), typeof(DifficultyModTexture),
                     typeof(DifficultyModKeyboard), typeof(DifficultyModHud), typeof(UpdateModDrawer), typeof(ModDrawerKeyboard),
                     typeof(PositionStandardMods), typeof(ApplyMirror), typeof(ChangeStandardRuleset),
                     typeof(Osu.StablePlus.Hook.Patches.UI.ShowModsInGameplay), typeof(SelectedBeatmapDetails),
                     typeof(SelectedBeatmapTooltipRate), typeof(InitializeStableScoring), typeof(StableScoreMenu),
                     typeof(ModeRateCalls), typeof(ModeRateConstants), typeof(InitializeOtherModeScoring) })
        {
            TestContext.WriteLine("Installing " + type.Name);
            new Osu.StablePlus.Hook.Patches.OsuPatchProcessor(harmony, type).Patch();
        }
        NativeStableScoring.VerifyInstallation();
        ModeRateTiming.VerifyInstallation();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (harmony == null) return;
        // Remove each wrapper atomically. Harmony 2.3 can emit invalid intermediate IL
        // while removing postfixes and our generic finalizers one at a time on .NET Framework.
        foreach (var method in harmony.GetPatchedMethods().ToArray())
            harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
    }

    [Test]
    public void LocatePlaybackAndReplayHooks()
    {
        Assert.That(CustomRatePlayback.Targets().Count(), Is.EqualTo(4));
        Assert.That(Score.ReadReplay.Reference, Is.Not.Null);
        Assert.That(Score.WriteReplay.Reference, Is.Not.Null);
        Assert.That(Score.ReplayVersion.Reference.FieldType, Is.EqualTo(typeof(int)));
        Assert.That(GameBase.Mode.Reference.FieldType.IsEnum, Is.True);
        Assert.That(Enum.GetNames(GameBase.Mode.Reference.FieldType), Does.Contain("SelectPlay"));
    }

    [Test]
    public void NativeScoringHooksResolveAndPreservePhysicalEditFormula()
    {
        Assert.That(NativeStableScoring.Ready, Is.True);
        Assert.That(NativeStableScoring.ScoreField.FieldType, Is.EqualTo(typeof(int)));
        var map = CreateSelectionTestMap();
        var baseline = NativeStableScoring.EditedDifficulty(map, null);
        Assert.That(NativeStableScoring.EditedDifficulty(map, new DifficultySettings(null, null, 0, null)), Is.EqualTo(baseline));
        var original = DifficultyControl.Default(map, 1);
        var altered = NativeStableScoring.EditedDifficulty(map, new DifficultySettings(null, 0, null, null));
        Assert.That(DifficultyControl.Default(map, 1), Is.EqualTo(original));
        DifficultyControl.Fields[1].SetValue(map, 0f);
        Assert.That(NativeStableScoring.EditedDifficulty(map, null), Is.EqualTo(altered));
    }

    [Test]
    public void ScoringSnapshotControlsAwardsAndIgnoresLaterPreferences()
    {
        var map = CreateSelectionTestMap();
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        var ruleset = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
            Osu.StablePlus.Stubs.GameModes.Play.Rulesets.Ruleset.Class.Reference.Assembly.GetTypes().First(t => !t.IsAbstract &&
                t.IsSubclassOf(Osu.StablePlus.Stubs.GameModes.Play.Rulesets.Ruleset.Class.Reference)));
        Osu.StablePlus.Stubs.GameModes.Play.Rulesets.Ruleset.CurrentScore.Set(ruleset, score);
        Score.Beatmap.Set(score, map);
        void Mods(int mods)
        {
            var wrapper = Score.EnabledMods.Reference.FieldType;
            var convert = wrapper.GetMethods().Single(m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
            Score.EnabledMods.Set(score, convert.Invoke(null, [Enum.ToObject(Osu.StablePlus.Stubs.Root.Mods.Type.Reference, mods)]));
        }
        var enabled = StableScoringOptions.Enabled.Value;
        try
        {
            Mods(64 | 128);
            RateControl.Remember(score, new RateSettings(1.6, scoring: new StableScoringSettings(1)));
            Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(306));
            StableScoringOptions.Enabled.Value = false;
            Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(306));
            RateControl.Remember(score, new RateSettings(1.6));
            Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(300));
            RateControl.Remember(score, new RateSettings(1.6, scoring: new StableScoringSettings(0)));
            Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(300));
            Mods(64);
            RateControl.Remember(score, new RateSettings(1.25, difficulty: new DifficultySettings(null, 0, 10, null), scoring: new StableScoringSettings(1)));
            var expected = NativeStableScoring.EditedDifficulty(map, RateControl.ForScore(score).Difficulty);
            Assert.That(NativeStableScoring.ResolveDifficulty(5, ruleset), Is.EqualTo(expected));
            Assert.That(RateControl.ForScore(score).Scoring!.DifficultyFactor, Is.EqualTo(expected));
            Assert.That(NativeStableScoring.ScaleModMultiplier(1.12, ruleset), Is.EqualTo(1.06).Within(1e-12));
            Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(300));
            Mods(64 | 128 | StableScoreMath.ScoreV2);
            Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(300));
            Assert.That(NativeStableScoring.ResolveDifficulty(5, ruleset), Is.EqualTo(5));
            Mods(64 | 128);
            StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.ScoreMode.FieldType, 3));
            Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(300));
        }
        finally { StableScoringOptions.Enabled.Value = enabled; }
    }

    [Test]
    public void SelectionDetailsHooksKeepNativeLabelsAndCounts()
    {
        var patched = SelectedBeatmapDetails.Transpiler(PatchProcessor.GetOriginalInstructions(SelectionDetails.Update)).ToArray();
        Assert.That(patched.Count(i => i.Calls(AccessTools.Method(typeof(SelectionDetails), nameof(SelectionDetails.Rate)))), Is.EqualTo(2));
        foreach (var method in new[] { nameof(SelectionDetails.Describe), nameof(SelectionDetails.DescribeTooltip), nameof(SelectionDetails.Time) })
            Assert.That(patched.Count(i => i.Calls(AccessTools.Method(typeof(SelectionDetails), method))), Is.EqualTo(1));
        var original = "CS:3.8 AR:9.4▲ OD:8.5▲ HP:5.8 Star Rating:9.87★\nLocal offset: +10ms";
        var result = SelectionDetails.Format(original, new double[] { 5.8, 0, 9.5, 9 }, 1.25, "7.25", "512.4");
        Assert.That(result, Is.EqualTo("CS:0 AR:10.2 OD:9.87 HP:5.8 Star Rating:7.25★ Max PP:512.4pp\nLocal offset: +10ms"));
        Assert.That(SelectionDetails.Format(original, new double[] { 5.8, 0, 5, 5 }, 0.5, "…", "…"), Does.Contain("AR:-5 OD:-3.33"));
    }

    private static object CreateSelectionTestMap(string? filename = null)
    {
        var path = filename ?? Environment.GetEnvironmentVariable("OSU_TEST_MAP");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set OSU_TEST_MAP to a standard .osu file.");
        // The test process has no song database; initialise its empty map list.
        var database = DifficultyControl.CurrentBeatmap.DeclaringType!;
        foreach (var mapList in database.GetFields(DifficultyControl.All).Where(f => f.IsStatic && f.FieldType.IsGenericType &&
            f.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>) &&
            f.FieldType.GetGenericArguments()[0] == Osu.StablePlus.Stubs.GameplayElements.Beatmaps.Beatmap.Class.Reference))
            if (mapList.GetValue(null) == null) mapList.SetValue(null, Activator.CreateInstance(mapList.FieldType));
        var timingIl = MethodReader.GetInstructions(DifficultyControl.Timing).ToArray();
        var viewportField = timingIl.Select(i => i.Operand).OfType<FieldInfo>().First(f => f.IsStatic && !f.FieldType.IsPrimitive);
        var viewport = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(viewportField.FieldType);
        var widthGetter = timingIl.Select(i => i.Operand).OfType<MethodInfo>().First(m => m.DeclaringType == viewportField.FieldType);
        ((FieldInfo)MethodReader.GetInstructions(widthGetter).Single(i => i.Opcode == Ldfld).Operand).SetValue(viewport, 512f);
        var scaleGetter = timingIl.Select(i => i.Operand).OfType<MethodInfo>().First(m => m.DeclaringType == viewportField.FieldType && m != widthGetter);
        var heightGetter = MethodReader.GetInstructions(scaleGetter).Select(i => i.Operand).OfType<MethodInfo>().Single();
        ((FieldInfo)MethodReader.GetInstructions(heightGetter).Single(i => i.Opcode == Ldfld).Operand).SetValue(viewport, 384f);
        viewportField.SetValue(null, viewport);
        var map = Osu.StablePlus.Stubs.GameplayElements.Beatmaps.Beatmap.Constructor.Invoke([path]);
        var starCache = map.GetType().GetFields(DifficultyControl.All).Single(f => f.FieldType == typeof(System.Collections.Generic.Dictionary<int, float>[]));
        starCache.SetValue(map, Enumerable.Range(0, 4).Select(_ => new System.Collections.Generic.Dictionary<int, float>()).ToArray());
        return map;
    }

    [Test]
    public void SelectionStarsUsePrivateDifficultyAndArbitraryRate()
    {
        Assert.That(Harmony.GetPatchInfo(SelectionStars.SetRate), Is.Null,
            "Custom star rates must not detour stable's rate helper.");
        var map = CreateSelectionTestMap();
        var defaults = Enumerable.Range(0, 4).Select(i => DifficultyControl.Default(map, i)).ToArray();
        var normal = SelectionStars.Calculate(map, 0, 1, null);
        var fast = SelectionStars.Calculate(map, Mods.DoubleTime, 1.25, null);
        var slow = SelectionStars.Calculate(map, Mods.HalfTime, 0.5, null);
        var large = SelectionStars.Calculate(map, Mods.DoubleTime, 1.25, new DifficultySettings(null, 0, null, null));
        TestContext.WriteLine($"Stars: NM {normal}, DT1.25 {fast}, HT0.5 {slow}, DT1.25+CS0 {large}");
        Assert.That(normal, Is.GreaterThan(0));
        Assert.That(fast, Is.GreaterThan(normal));
        Assert.That(slow, Is.LessThan(normal).And.GreaterThan(0));
        Assert.That(large, Is.LessThan(fast).And.GreaterThan(0));
        Assert.That(Enumerable.Range(0, 4).Select(i => DifficultyControl.Default(map, i)), Is.EqualTo(defaults));
        // A private calculation must agree with the unmodified native path at default rates.
        var factory = map.GetType().GetMethods(DifficultyControl.All).Single(m =>
            m.ReturnType == SelectionStars.CalculateMethod.DeclaringType && m.GetParameters().Length == 1);
        var native = factory.Invoke(map, [Enum.ToObject(factory.GetParameters()[0].ParameterType, 0)]);
        try
        {
            foreach (var mods in new[] { 0, Mods.DoubleTime, Mods.HalfTime, Mods.HardRock | Mods.Hidden,
                         Mods.Easy | Mods.HalfTime, Mods.Flashlight | Mods.DoubleTime })
            {
                var rate = (mods & Mods.DoubleTime) != 0 ? 1.5 : (mods & Mods.HalfTime) != 0 ? 0.75 : 1;
                var expected = (double)SelectionStars.CalculateMethod.Invoke(native, [Enum.ToObject(Mods.Type.Reference, mods), null, null]);
                Assert.That(SelectionStars.Calculate(map, mods, rate, null), Is.EqualTo(expected).Within(0.000001), "Mods: " + mods);
            }
        }
        finally
        {
            SelectionStars.CalculateMethod.DeclaringType!.GetMethods(DifficultyControl.All).Single(m =>
                m.IsPublic && m.IsVirtual && m.ReturnType == typeof(void) && m.GetParameters().Length == 0).Invoke(native, null);
        }
        var modified = new DifficultySettings(2, 0, 9.5, 9);
        var combined = SelectionStars.Calculate(map, Mods.DoubleTime | Mods.Hidden | Mods.Relax, 1.25, modified);
        Assert.That(SelectionStars.Calculate(map, Mods.DoubleTime | Mods.Hidden | Mods.Relax | MirrorSettings.Flag, 1.25, modified),
            Is.EqualTo(combined).Within(0.000001));
        var savedMods = ModManager.ModStatus.Get();
        var savedDifficulty = DifficultyControl.Selected;
        var savedMode = ChangeStandardRuleset.ModeField.GetValue(null);
        var localise = MethodReader.GetInstructions(SelectionDetails.Tooltip).Select(i => i.Operand).OfType<MethodInfo>()
            .First(m => m.ReturnType == typeof(string) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
        harmony.Patch(localise, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(TestTooltipTranslation)));
        try
        {
            ChangeStandardRuleset.ModeField.SetValue(null, Enum.ToObject(ChangeStandardRuleset.ModeField.FieldType, 0));
            ModManager.ModStatus.Set(Mods.DoubleTime | DifficultyControl.Flag);
            CustomRateOptions.Set(1.25, false);
            DifficultyControl.Selected = modified;
            var tooltip = SelectionDetails.DescribeTooltip(map);
            TestContext.WriteLine("Tooltip: " + tooltip);
            Assert.That(tooltip, Does.Contain("420")); // AR9.5: 525 / 1.25 milliseconds.
            Assert.That(tooltip, Does.Contain("20.4")); // OD9: (26 - 0.5) / 1.25.
            Assert.That(SelectionDetails.TooltipMap, Is.Null);
            Assert.That(SelectionDetails.TooltipRate, Is.Null);
            Assert.That(SelectionDetails.Time(150000), Is.EqualTo(120000));
            Assert.That(SelectionDetails.Rate(1.5f), Is.EqualTo(1.25f));
            var description = (string)SelectionDetails.Description.Invoke(map, null);
            TestContext.WriteLine("Native description: " + description);
            Assert.That(SelectionDetails.Format(description, SelectionDetails.Configured(map, ModManager.ModStatus.Get(), modified), 1.25, "7.25", "512.4"),
                Does.Contain("CS:0").And.Contain("AR:10.2").And.Contain("OD:9.87").And.Contain("HP:2").And.Contain("Star Rating: 7.25").And.Contain("Max PP:512.4pp"));
            foreach (var mode in new[] { 1, 2, 3 })
            {
                ChangeStandardRuleset.ModeField.SetValue(null, Enum.ToObject(ChangeStandardRuleset.ModeField.FieldType, mode));
                Assert.That(SelectionDetails.Describe(map), Does.Contain("Star Rating:"));
                Assert.That(SelectionDetails.Rate(1.5f), Is.EqualTo(1.25f));
            }
        }
        finally
        {
            harmony.Unpatch(localise, HarmonyPatchType.All, harmony.Id);
            ModManager.ModStatus.Set(savedMods);
            DifficultyControl.Selected = savedDifficulty;
            ChangeStandardRuleset.ModeField.SetValue(null, savedMode);
            CustomRateOptions.ObserveMods(0);
        }
    }

    [TestCase(true, true)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void StarReductionOptionsPersistIndependently(bool relax, bool autopilot)
    {
        var options = new StarRatingOptions();
        var previous = new Settings();
        options.Save(previous);
        try
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Settings));
            using var oldConfig = new StringReader("<Settings />");
            options.Load((Settings)serializer.Deserialize(oldConfig));
            Assert.That(StarRatingOptions.ReduceRelax.Value, Is.False);
            Assert.That(StarRatingOptions.ReduceAutopilot.Value, Is.False);
            options.Load(new Settings { ApplyRelaxStarReduction = relax, ApplyAutopilotStarReduction = autopilot });
            var saved = new Settings();
            options.Save(saved);
            using var writer = new StringWriter();
            serializer.Serialize(writer, saved);
            options.Load(new Settings());
            using var reader = new StringReader(writer.ToString());
            options.Load((Settings)serializer.Deserialize(reader));
            Assert.That(StarRatingOptions.ReduceRelax.Value, Is.EqualTo(relax));
            Assert.That(StarRatingOptions.ReduceAutopilot.Value, Is.EqualTo(autopilot));
        }
        finally { options.Load(previous); }
    }

    [Test]
    public void StarReductionPreferencesRefreshCachedDisplayWithoutChangingSelectedMods()
    {
        double Official(object target, int flags, double rate, DifficultySettings? da) => PerformanceClient.CalculateAsync(new CalculationRequest
        {
            MapPath = Osu.StablePlus.Stubs.GameplayElements.Beatmaps.Beatmap.GetBeatmapPath(target)!,
            Mods = flags,
            Rate = rate,
            Difficulty = da?.Values ?? new double?[4],
            StarsOnly = true
        }).GetAwaiter().GetResult().Stars;
        var map = CreateSelectionTestMap();
        var options = new StarRatingOptions();
        var previous = new Settings();
        options.Save(previous);
        var savedMods = ModManager.ModStatus.Get();
        try
        {
            foreach (var scenario in new[] {
                new { Mods = 0, Rate = 1d, Difficulty = (DifficultySettings?)null },
                new { Mods = Mods.Hidden | Mods.DoubleTime | DifficultyControl.Flag, Rate = 1.25,
                    Difficulty = (DifficultySettings?)new DifficultySettings(null, 0, 9.5, 9) },
                new { Mods = Mods.HalfTime, Rate = 0.6, Difficulty = (DifficultySettings?)null } })
            {
                var baseline = Official(map, scenario.Mods, scenario.Rate, scenario.Difficulty);
                foreach (var assistance in new[] { Mods.Relax, Mods.Relax2 })
                {
                    var mods = scenario.Mods | assistance;
                    ModManager.ModStatus.Set(mods);
                    var reduced = Official(map, mods, scenario.Rate, scenario.Difficulty);
                    Assert.That(reduced, Is.GreaterThan(0).And.LessThan(baseline), "Assistance: " + assistance);
                    TestContext.WriteLine($"Star options: mods {mods}, rate {scenario.Rate}: {reduced} reduced / {baseline} unreduced");
                    // Revisit both choices to exercise cached results as well as fresh calculations.
                    foreach (var relax in new[] { true, false, true })
                        foreach (var autopilot in new[] { true, false, true })
                        {
                            options.Load(new Settings { ApplyRelaxStarReduction = relax, ApplyAutopilotStarReduction = autopilot });
                            var expected = (assistance == Mods.Relax ? relax : autopilot) ? reduced : baseline;
                            string display = "…";
                            Assert.That(System.Threading.SpinWait.SpinUntil(() =>
                            {
                                display = SelectionStars.Get(map, mods, scenario.Rate, scenario.Difficulty);
                                return display != "…";
                            }, 10000), Is.True, "Star calculation did not finish");
                            Assert.That(display, Is.EqualTo(expected.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                                $"Mods {mods}; RX reduction {relax}, AP reduction {autopilot}");
                            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(mods));
                        }
                }
            }
        }
        finally { options.Load(previous); ModManager.ModStatus.Set(savedMods); }
    }

    private static bool TestTooltipTranslation(object __0, ref string __result)
    {
        if (__0.ToString() != "SongSelection_DifficultyInfo_Tooltip_Osu") return true;
        // Supply localisation without starting the game's graphics/localisation UI.
        __result = "AR {0}ms; 300 {1}ms; 100 {2}ms; 50 {3}ms; Spinner {4}; Radius {5}";
        return false;
    }

    [Test]
    public void LivePreviewContinuationOnlyChangesTheRateCheck()
    {
        var original = PatchProcessor.GetOriginalInstructions(AudioEngine.LoadAudioForPreview.Reference);
        var patched = TrackUpdatePreviewMusic.Transpiler(original.Select(i => new CodeInstruction(i))).ToArray();
        Assert.That(patched.Length, Is.EqualTo(original.Count));
        var differences = original.Zip(patched, (a, b) => a.opcode != b.opcode || !Equals(a.operand, b.operand)).Count(x => x);
        Assert.That(differences, Is.EqualTo(1));
        Assert.That(patched.Count(i => i.Calls(AccessTools.Method(typeof(TrackUpdatePreviewMusic), nameof(TrackUpdatePreviewMusic.ContinuationRate)))), Is.EqualTo(1));
    }

    [TestCase(0, 0f)]
    [TestCase(20, 0.2f)]
    [TestCase(100, 1f)]
    [TestCase(-5, 0f)]
    [TestCase(120, 1f)]
    public void GameplayModOpacityUsesNativePercentageBinding(int percent, float expected)
    {
        var options = new Osu.StablePlus.Hook.Patches.UI.GameplayModIconOptions();
        var saved = Osu.StablePlus.Hook.Patches.UI.GameplayModIconOptions.Percent;
        try
        {
            options.Load(new Settings());
            Assert.That(Osu.StablePlus.Hook.Patches.UI.ShowModsInGameplay.FinalOpacity(), Is.Zero);
            options.Load(new Settings { GameplayModIconOpacity = percent });
            Assert.That(Osu.StablePlus.Hook.Patches.UI.ShowModsInGameplay.FinalOpacity(), Is.EqualTo(expected).Within(0.0001));
            var config = new Settings();
            options.Save(config);
            Assert.That(config.GameplayModIconOpacity, Is.EqualTo((int)(expected * 100)));
        }
        finally { Osu.StablePlus.Hook.Patches.UI.GameplayModIconOptions.Percent = saved; }
    }

    [Test]
    public void AllNativeModIconBuildersIncludeDifficultyAdjust()
    {
        var methods = DifficultyModHud.DisplayMethods().ToArray();
        Assert.That(methods.Length, Is.EqualTo(4));
        Assert.That(methods, Does.Contain(Player.OnLoadComplete.Reference));
        var values = DifficultyModHud.DisplayValues(Mods.Type.Reference).Cast<object>().Select(Convert.ToInt32).ToArray();
        Assert.That(values.Count(value => value == DifficultyControl.Flag), Is.EqualTo(1));
        foreach (var method in methods)
        {
            var output = DifficultyModHud.Transpiler(PatchProcessor.GetOriginalInstructions(method)
                .Select(i => new CodeInstruction(i))).ToList();
            Assert.That(output.Any(i => i.Calls(AccessTools.Method(typeof(DifficultyModHud), nameof(DifficultyModHud.DisplayValues)))), Is.True);
            Assert.That(output.Any(i => i.opcode == Cgt_Un), Is.True);
            for (var i = 2; i < output.Count; i++)
                Assert.That(output[i - 2].opcode == And && output[i - 1].opcode == Ldc_I4_0 && output[i].opcode == Cgt, Is.False);
        }
    }

    [TestCase(Mods.Hidden | Mods.Relax | DifficultyControl.Flag, Mods.Hidden, Mods.Relax, DifficultyControl.Flag)]
    [TestCase(Mods.Hidden | Mods.Relax | Mods.HardRock, Mods.Hidden, Mods.HardRock, Mods.Relax)]
    public void HudIconSlotsExcludeInvisibleCompositeMasks(int active, int first, int second, int third)
    {
        var displayed = DifficultyModHud.DisplayValues(Mods.Type.Reference).Cast<object>()
            .Select(Convert.ToInt32).Where(value => (value & active) != 0).ToArray();
        Assert.That(displayed, Is.EqualTo(new[] { first, second, third }));
        // Native HUD assigns each emitted icon 10 logical pixels and 500 ms.
        // Both combinations must have exactly three consecutive slots.
        Assert.That(displayed.Length, Is.EqualTo(3));
    }

    [Test]
    public void IconEnumerationPreservesNativeIndividualModOrder()
    {
        var native = Enum.GetValues(Mods.Type.Reference).Cast<object>().Select(Convert.ToInt32)
            .Where(value => value > 0 && (value & (value - 1)) == 0).ToArray();
        var displayed = DifficultyModHud.DisplayValues(Mods.Type.Reference).Cast<object>().Select(Convert.ToInt32).ToArray();
        Assert.That(displayed.Where(value => value > 0), Is.EqualTo(native));
        Assert.That(displayed.Last(), Is.EqualTo(DifficultyControl.Flag));
        Assert.That(DifficultyModHud.DisplayValues(typeof(DayOfWeek)), Is.EqualTo(Enum.GetValues(typeof(DayOfWeek))));
    }

    [TestCase("HD,DT,Relax", false, "HD,DT(1.7x),Relax,DA(CS5,AR7.5,OD6.5)")]
    [TestCase("HD,DT,Relax", true, "HD,DT(1.7x),RX,DA(CS5,AR7.5,OD6.5)")]
    [TestCase("", false, "DA(CS5,AR7.5,OD6.5)")]
    public void DifficultyLabelsUseCommaSeparatedMods(string input, bool shortRelax, string expected)
    {
        var previous = ModLabelOptions.RelaxAsRx.Value;
        try
        {
            ModLabelOptions.RelaxAsRx.Value = shortRelax;
            var score = Activator.CreateInstance(Score.Class.Reference, true)!;
            var wrapper = Score.EnabledMods.Reference.FieldType;
            var conversion = wrapper.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
            Score.EnabledMods.Set(score, conversion.Invoke(null, [Enum.ToObject(Mods.Type.Reference,
                input.Contains("DT") ? Mods.DoubleTime | Mods.Relax : 0)]));
            RateControl.Remember(score, new RateSettings(1.7, false, new DifficultySettings(null, 5, 7.5, 6.5)));
            Assert.That(LocalScoreRateDisplay.Format(input, score), Is.EqualTo(expected));
            Assert.That(DifficultyModHud.DisplayMods(score) & DifficultyControl.Flag, Is.Not.Zero);
            var settings = new Settings();
            new ModLabelOptions().Save(settings);
            Assert.That(settings.ShowRelaxAsRx, Is.EqualTo(shortRelax));
        }
        finally { ModLabelOptions.RelaxAsRx.Value = previous; }
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void ModDetailTogglesAndDefaultFiltering(bool showRate, bool showDifficulty)
    {
        var previousRate = ModLabelOptions.ShowRate.Value;
        var previousDifficulty = ModLabelOptions.ShowDifficulty.Value;
        try
        {
            ModLabelOptions.ShowRate.Value = showRate;
            ModLabelOptions.ShowDifficulty.Value = showDifficulty;
            var map = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Osu.StablePlus.Stubs.GameplayElements.Beatmaps.Beatmap.Class.Reference);
            foreach (var field in DifficultyControl.Fields) field.SetValue(map, 5f);
            var settings = new RateSettings(1.15, false, new DifficultySettings(5, 1.4, 7.8, null));
            var rate = showRate ? "(1.15x)" : "";
            var da = showDifficulty ? "(CS1.4,AR7.8)" : "";
            Assert.That(ModLabelFormatter.Format("DT,AP", Mods.DoubleTime | Mods.Relax2, settings, map, false, false),
                Is.EqualTo("DT" + rate + ",AP,DA" + da));
            Assert.That(ModLabelFormatter.Format("DoubleTime,AutoPilot", Mods.DoubleTime | Mods.Relax2, settings, map, true, true),
                Is.EqualTo("DoubleTime" + (showRate ? " " + rate : "") + ", AutoPilot, DifficultyAdjust" + (showDifficulty ? " " + da : "")));
            Assert.That(ModLabelFormatter.Format("Hidden,DoubleTime", Mods.Hidden | Mods.DoubleTime, settings, map, true, false),
                Is.EqualTo("Hidden,DoubleTime" + rate + ",DifficultyAdjust" + da));
            var config = new Settings();
            new ModLabelOptions().Save(config);
            Assert.That(config.ShowRateInModLabels, Is.EqualTo(showRate));
            Assert.That(config.ShowDifficultyInModLabels, Is.EqualTo(showDifficulty));
        }
        finally { ModLabelOptions.ShowRate.Value = previousRate; ModLabelOptions.ShowDifficulty.Value = previousDifficulty; }
    }

    [Test]
    public void HoverLabelIsIdentifiedByItsNativeFormatter()
    {
        var instructions = MethodReader.GetInstructions(LocalScoreRateDisplay.Target()).ToArray();
        Assert.That(instructions.Any(i => Equals(i.Operand, SelectedRateDisplay.LongFormatter)), Is.True);
    }

    [TestCase(Mods.DoubleTime, "HD,DT,RX", "HD,DT,RX")]
    [TestCase(Mods.DoubleTime | Mods.Nightcore, "HD,NC,RX", "HD,NC,RX")]
    public void DefaultRateIsOmittedFromRankings(int mods, string text, string expected)
    {
        Assert.That(ModLabelFormatter.Format(text, mods, new RateSettings(1.5, false), null, false, false), Is.EqualTo(expected));
        var name = SelectedRateDisplay.RateName(mods);
        Assert.That(ModLabelFormatter.Format(name, mods, new RateSettings(1.5, false), null, true, true), Is.EqualTo(name));
    }

    [Test]
    public void DrainCalculationsRemainNativeAndHpIsRestoredAfterFailure()
    {
        var targets = ApplyDifficultyAdjust.Targets().ToArray();
        foreach (var method in ApplyDifficultyAdjust.Readers().OfType<MethodInfo>().Where(m => m.ReturnType == typeof(double)))
        {
            Assert.That(targets, Does.Not.Contain(method));
            Assert.That(Harmony.GetPatchInfo(method), Is.Null);
        }
        Assert.That(targets.OfType<MethodInfo>().Any(m => m.ReturnType == typeof(double)), Is.False);
        Assert.That(DifficultyDrainScope.Targets().Count(), Is.EqualTo(1));
        var map = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Osu.StablePlus.Stubs.GameplayElements.Beatmaps.Beatmap.Class.Reference);
        DifficultyControl.Fields[0].SetValue(map, 5f);
        DifficultyDrainScope.WithHp(map, 8, () => Assert.That(DifficultyControl.Default(map, 0), Is.EqualTo(8)));
        Assert.That(DifficultyControl.Default(map, 0), Is.EqualTo(5));
        Assert.Throws<InvalidOperationException>(() => DifficultyDrainScope.WithHp(map, 2, () =>
        {
            Assert.That(DifficultyControl.Default(map, 0), Is.EqualTo(2));
            throw new InvalidOperationException();
        }));
        Assert.That(DifficultyControl.Default(map, 0), Is.EqualTo(5));
    }

    [Test]
    public void OpenModMenuRestoresMissingKeyboardFocusWithoutStealingAnotherDialog()
    {
        var previous = NativeModMenu.FocusedDialog.GetValue(null);
        var menu = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(NativeModMenu.Menu);
        var other = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(NativeModMenu.Menu);
        try
        {
            NativeModMenu.FocusedDialog.SetValue(null, null);
            NativeModMenu.RestoreKeyboardFocus(menu);
            Assert.That(NativeModMenu.FocusedDialog.GetValue(null), Is.Null);
            NativeModMenu.IsOpen.SetValue(menu, true);
            NativeModMenu.RestoreKeyboardFocus(menu);
            Assert.That(NativeModMenu.FocusedDialog.GetValue(null), Is.SameAs(menu));
            NativeModMenu.FocusedDialog.SetValue(null, other);
            NativeModMenu.RestoreKeyboardFocus(menu);
            Assert.That(NativeModMenu.FocusedDialog.GetValue(null), Is.SameAs(other));
        }
        finally { NativeModMenu.FocusedDialog.SetValue(null, previous); }
    }

    [TestCase(1.01)]
    [TestCase(1.5)]
    [TestCase(1.7)]
    [TestCase(2.0)]
    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(0.99)]
    public void TimingCheckUsesPlayRateAndPreservesDriftDetection(double speed)
    {
        var original = PatchProcessor.GetOriginalInstructions(CustomRateTiming.Target()).ToList();
        var patched = CustomRateTiming.Transpiler(original.Select(i => new CodeInstruction(i))).ToList();
        Assert.That(patched.Count, Is.EqualTo(original.Count));
        var changed = original.Select((i, index) => index).Where(index =>
            original[index].opcode != patched[index].opcode || !Equals(original[index].operand, patched[index].operand)).ToArray();
        Assert.That(changed.Length, Is.EqualTo(2));
        Assert.That(original[changed[0]].operand, Is.EqualTo(1.5f));
        var replacement = (MethodInfo)patched[changed[0]].operand;

        var previous = Player.CurrentScore.Get();
        try
        {
            var score = Activator.CreateInstance(Score.Class.Reference, true)!;
            RateControl.Remember(score, new RateSettings(speed, false));
            Player.CurrentScore.Set(score);
            new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.3 });
            var expectedRate = (float)replacement.Invoke(null, null)!;
            Assert.That(expectedRate, Is.EqualTo((float)speed));
            // Stable's unchanged comparison: audio elapsed / rate versus wall time.
            var audioElapsed = 1700f;
            var wallElapsed = audioElapsed / (float)speed;
            var normalized = audioElapsed * (1f / expectedRate);
            Assert.That(Math.Abs(normalized - wallElapsed), Is.LessThan(1f));
            Assert.That(Math.Abs(normalized - (wallElapsed + 80f)), Is.GreaterThan(60f));
        }
        finally
        {
            Player.CurrentScore.Set(previous);
            new CustomRateOptions().Load(new Settings());
        }
    }

    [Test]
    public void LocateNativeModMenuControls()
    {
        Assert.That(MirrorDropdown.Constructor.GetParameters().Length, Is.EqualTo(5));
        Assert.That(StandardModLayout.ScoreMode.FieldType, Is.EqualTo(StandardModLayout.PlayMode.ReturnType));
        Assert.That(NativeDrawerSlider.Hovered.FieldType, Is.EqualTo(typeof(bool)));
        Assert.That(NativeDrawerSlider.Hovered.DeclaringType, Is.EqualTo(NativeModMenu.Slider.DeclaringType));
        Assert.That(NativeDrawerSlider.Dragging.ReturnType, Is.EqualTo(typeof(bool)));
        Assert.That(NativeModMenu.SliderValue.FieldType, Is.EqualTo(typeof(double)));
        Assert.That(NativeModMenu.SliderStep, Is.Not.EqualTo(NativeModMenu.SliderValue));
        Assert.That(NativeModMenu.SliderKeyboard.FieldType, Is.EqualTo(typeof(bool)));
        Assert.That(NativeModMenu.SliderVisible.GetParameters().Single().ParameterType, Is.EqualTo(typeof(bool)));
        Assert.That(NativeModMenu.SliderChanged.FieldType.GetMethod("Invoke")!.GetParameters()
            .Select(p => p.ParameterType), Is.EqualTo(new[] { typeof(bool) }));
        Assert.That(NativeModMenu.CheckboxChanged.FieldType.GetMethod("Invoke")!.GetParameters()
            .Select(p => p.ParameterType), Is.EqualTo(new[] { typeof(object), typeof(bool) }));
        Assert.That(NativeModMenu.Dispose.IsFamily, Is.True);
    }

    [Test]
    public void MenuOpeningFadeCanBeCancelledBeforeRenderingHiddenControls()
    {
        var fadeIn = MethodReader.GetInstructions(NativeModMenu.Open).Select(i => i.Operand)
            .OfType<MethodInfo>().Single(m => m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(int) && m.ReturnType != typeof(void) &&
                !m.DeclaringType!.IsGenericType);
        var drawable = fadeIn.DeclaringType!;
        var sprite = Activator.CreateInstance(drawable, [true])!;
        var alpha = MethodReader.GetInstructions(fadeIn).Where(i => i.Opcode == Stfld)
            .Select(i => i.Operand).OfType<FieldInfo>().Single(f => f.FieldType == typeof(float));
        var animations = MethodReader.GetInstructions(fadeIn).Where(i => i.Opcode == Ldfld)
            .Select(i => i.Operand).OfType<FieldInfo>().Distinct().Single(f =>
                typeof(System.Collections.IList).IsAssignableFrom(f.FieldType));

        NativeModMenu.SetVisible(sprite, false);
        // Native Dialog.Open adds this fade even though alpha is already zero.
        fadeIn.Invoke(sprite, [300]);
        Assert.That(((System.Collections.IList)animations.GetValue(sprite)).Count, Is.GreaterThan(0));
        NativeModMenu.SetVisible(sprite, false);
        Assert.That(alpha.GetValue(sprite), Is.EqualTo(0f));
        Assert.That(((System.Collections.IList)animations.GetValue(sprite)).Count, Is.Zero);
        NativeModMenu.SetVisible(sprite, true);
        Assert.That(alpha.GetValue(sprite), Is.EqualTo(1f));
        Assert.That(Harmony.GetPatchInfo(NativeModMenu.Open).Postfixes.Any(p =>
            p.PatchMethod.DeclaringType == typeof(RestoreModMenuRateVisibility)), Is.True);
    }

    [Test]
    public void OnlyDtAndHtModConstantsChange()
    {
        var replacedPercent = 0;
        var preservedSpeedButton = false;
        foreach (var target in CustomRatePlayback.Targets())
        {
            var before = PatchProcessor.GetOriginalInstructions(target).ToList();
            var after = CustomRatePlayback.Transpiler(before.Select(i => new CodeInstruction(i)), target).ToList();
            replacedPercent += after.Count(i => i.Calls(AccessTools.Method(typeof(RateControl), nameof(RateControl.GameplayPercent))));
            if (before.Count(i => i.opcode == Ldc_R8 && Equals(i.operand, 1.5)) == 4)
            {
                Assert.That(after.Count(i => i.opcode == Ldc_R8 && Equals(i.operand, 1.5)), Is.EqualTo(4));
                preservedSpeedButton = true;
            }
        }
        Assert.That(replacedPercent, Is.EqualTo(4));
        Assert.That(preservedSpeedButton, Is.True);
    }

    [Test]
    public void PlaybackUsesImportedReplayEvenWhenMenuRateDiffers()
    {
        using var source = OpenUserReplay();
        var replay = ReadScore(source);
        Assert.That(Score.IsLoadedReplay.Get(replay), Is.True);
        new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.3, DoubleTimeAdjustPitch = true });
        var freshPlaybackScore = Activator.CreateInstance(Score.Class.Reference, true)!;
        var previous = Player.CurrentScore.Get();
        var previousReplay = Player.ReplayScore.Get();
        var replayFlag = MethodReader.GetInstructions(Player.IsReplay.Reference)
            .Where(i => i.Opcode == Ldsfld).Select(i => i.Operand).OfType<FieldInfo>()
            .First(f => f.FieldType == typeof(bool));
        var previousFlag = replayFlag.GetValue(null);
        try
        {
            Player.ReplayScore.Set(replay);
            replayFlag.SetValue(null, true);
            Assert.That(Player.IsReplay.Invoke(), Is.True);
            Player.CurrentScore.Set(freshPlaybackScore);
            CaptureGameplayRate.Capture();
            Assert.That(RateControl.GameplayPercent(), Is.EqualTo(170));
            Assert.That(RateControl.Current.AdjustPitch, Is.False);
            Assert.That(LocalScoreRateDisplay.Format("HD,DT,RX", replay), Is.EqualTo("HD,DT(1.7x),RX"));
            // Reopening settings or seeking must not overwrite the active play's snapshot.
            new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.9 });
            Assert.That(RateControl.GameplayMultiplier(), Is.EqualTo(1.7));
        }
        finally
        {
            Player.CurrentScore.Set(previous);
            Player.ReplayScore.Set(previousReplay);
            replayFlag.SetValue(null, previousFlag);
            new CustomRateOptions().Load(new Settings());
        }
    }

    [Test]
    public void CompletedPlayAndClonedExportKeepStartRate()
    {
        using var source = OpenUserReplay();
        var replay = ReadScore(source);
        var completed = Activator.CreateInstance(Score.Class.Reference, true)!;
        new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.8, DoubleTimeAdjustPitch = true });
        RateControl.CaptureGameplayScore(completed, null);
        // Stable fills mods/frames after creating its new gameplay score.
        Score.EnabledMods.Set(completed, Score.EnabledMods.Get(replay));
        Score.ReplayData.Set(completed, Score.ReplayData.Get(replay));
        var exported = ((ICloneable)completed).Clone();
        new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.2 });
        try
        {
            using var bytes = new MemoryStream();
            var writerType = Score.WriteReplay.Reference.GetParameters()[0].ParameterType;
            using var writer = (BinaryWriter)Activator.CreateInstance(writerType, bytes)!;
            Score.WriteReplay.Invoke(exported, [writer]);
            using var saved = new MemoryStream(bytes.ToArray());
            var result = ReplayRateMetadata.ReadReplay(saved)!;
            Assert.That(result.Speed, Is.EqualTo(1.8));
            Assert.That(result.AdjustPitch, Is.True);
            using var copy = new MemoryStream(bytes.ToArray());
            Assert.That(RateControl.ForScore(ReadScore(copy)).Speed, Is.EqualTo(1.8));
        }
        finally { new CustomRateOptions().Load(new Settings()); }
    }

    [Test]
    public void UnknownLegacyScoreNeverBorrowsMenuRate()
    {
        new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.9, DoubleTimeAdjustPitch = true });
        try
        {
            var score = Activator.CreateInstance(Score.Class.Reference, true)!;
            Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(1.5));
            Assert.That(RateControl.ForScore(score).AdjustPitch, Is.False);
        }
        finally { new CustomRateOptions().Load(new Settings()); }
    }

    [Test]
    public void ImportSaveReloadUserReplay()
    {
        var filename = Environment.GetEnvironmentVariable("OSU_TEST_REPLAY");
        if (string.IsNullOrEmpty(filename)) Assert.Ignore("Set OSU_TEST_REPLAY to the supplied DT 1.7x replay.");
        using var source = File.OpenRead(filename!);
        var score = ReadScore(source);
        Assert.That(RateControl.GetMods(score), Is.EqualTo(200));
        Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(1.7));
        Assert.That(RateControl.ForScore(score).AdjustPitch, Is.False);

        using var saved = new MemoryStream();
        var writerType = Score.WriteReplay.Reference.GetParameters()[0].ParameterType;
        using (var writer = (BinaryWriter)Activator.CreateInstance(writerType, saved)!)
        {
            Score.WriteReplay.Invoke(score, [writer]);
            var bytes = saved.ToArray();
            using var copy = new MemoryStream(bytes);
            var reloaded = ReadScore(copy);
            Assert.That(Score.ReplayVersion.Get(reloaded), Is.LessThan(30000000));
            Assert.That(RateControl.ForScore(reloaded).Speed, Is.EqualTo(1.7));
            using var standalone = new MemoryStream(bytes);
            Assert.That(ReplayRateMetadata.ReadReplay(standalone)!.Speed, Is.EqualTo(1.7));

            // A database score receives the same compressed buffer from a temporary replay Score.
            var databaseScore = Activator.CreateInstance(Score.Class.Reference, true)!;
            Score.ReplayData.Set(databaseScore, Score.ReplayData.Get(reloaded));
            Assert.That(RateControl.ForScore(databaseScore).Speed, Is.EqualTo(1.7));
        }
    }

    [TestCase(0.5, false, false)]
    [TestCase(0.75, true, false)]
    [TestCase(0.99, false, false)]
    [TestCase(0.6, true, true)]
    public void HalfTimeSaveExportImportKeepsRatePitchAndFrames(double speed, bool pitch, bool da)
    {
        using var source = OpenUserReplay();
        var score = ReadScore(source);
        var wrapper = Score.EnabledMods.Reference.FieldType;
        var conversion = wrapper.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
        Score.EnabledMods.Set(score, conversion.Invoke(null, [Enum.ToObject(Mods.Type.Reference, 256 | 8)]));
        var settings = new RateSettings(speed, pitch, da ? new DifficultySettings(null, 3.2, 9.1, null) : null);
        RateControl.Remember(score, settings);
        new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.9, HalfTimeSpeed = 0.6 });
        try
        {
            foreach (var export in new[] { false, true })
            {
                using var bytes = new MemoryStream();
                using var writer = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, bytes)!;
                ExportLazerReplay.Write(score, writer, export);
                var data = bytes.ToArray();
                using var copy = new MemoryStream(data);
                var imported = ReadScore(copy);
                var result = RateControl.ForScore(imported);
                Assert.That(result.Speed, Is.EqualTo(speed));
                Assert.That(result.AdjustPitch, Is.EqualTo(pitch));
                Assert.That(result.Difficulty?.Values, Is.EqualTo(settings.Difficulty?.Values));
                Assert.That(Score.ReplayData.Get(imported), Is.EqualTo(Score.ReplayData.Get(score)));
                Assert.That(BitConverter.ToInt32(data, 1) >= 30000001, Is.EqualTo(export));
                Assert.That(LocalScoreRateDisplay.Format("HD,HT", imported),
                    Is.EqualTo("HD,HT" + (speed == 0.75 ? "" : "(" + speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x)") + (da ? ",DA(CS3.2,AR9.1)" : "")));
            }
        }
        finally { new CustomRateOptions().Load(new Settings()); }
    }

    [Test]
    public void HalfTimeSelectionResetsIndependentlyAndPersistsPitch()
    {
        var options = new CustomRateOptions();
        try
        {
            options.Load(new Settings { DoubleTimeSpeed = 1.8, HalfTimeSpeed = 0.6, HalfTimeAdjustPitch = true });
            CustomRateOptions.ObserveMods(256);
            Assert.That(CustomRateOptions.ForMods(256).Speed, Is.EqualTo(0.6));
            Assert.That(CustomRateOptions.Selected.Speed, Is.EqualTo(1.5));
            CustomRateOptions.ObserveMods(64 | 512);
            Assert.That(CustomRateOptions.SelectedHalfTime.Speed, Is.EqualTo(0.75));
            Assert.That(CustomRateOptions.SelectedHalfTime.AdjustPitch, Is.True);
            var config = new Settings(); options.Save(config);
            Assert.That(config.HalfTimeAdjustPitch, Is.True);
            Assert.That(SelectedRateDisplay.Format("HalfTime", 256, 0.6), Is.EqualTo("HalfTime(0.6x)"));
            Assert.That(SelectedRateDisplay.Format("HalfTime", 256, 0.75), Is.EqualTo("HalfTime"));
        }
        finally { options.Load(new Settings()); }
    }

    private static object ReadScore(Stream stream)
    {
        var readerType = Score.ReadReplay.Reference.GetParameters()[0].ParameterType;
        using var reader = (BinaryReader)Activator.CreateInstance(readerType, stream)!;
        var mode = reader.ReadByte();
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.ScoreMode.FieldType, mode));
        Score.ReadReplay.Invoke(score, [reader, false]);
        return score;
    }

    [TestCase(0, 64, true)]
    [TestCase(1, 64, true)]
    [TestCase(2, 64, true)]
    [TestCase(0, 0, false)]
    [TestCase(1, 0, true)]
    [TestCase(2, 576, true)]
    public void MirrorReplayRoundTripAndNativeCoordinates(int axis, int rateMods, bool da)
    {
        using var source = OpenUserReplay();
        var score = ReadScore(source);
        var wrapper = Score.EnabledMods.Reference.FieldType;
        var conversion = wrapper.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
        Score.EnabledMods.Set(score, conversion.Invoke(null, [Enum.ToObject(Mods.Type.Reference, rateMods)]));
        var settings = new RateSettings(1.83, true, da ? new DifficultySettings(null, 3.2, 9.1, null) : null, (MirrorAxes)axis);
        RateControl.Remember(score, settings);
        MirrorSettings.Selected = (MirrorAxes)((axis + 1) % 3);
        foreach (var export in new[] { false, true })
        {
            using var bytes = new MemoryStream();
            using var writer = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, bytes)!;
            ExportLazerReplay.Write(score, writer, export);
            var data = bytes.ToArray();
            using var copy = new MemoryStream(data);
            var imported = ReadScore(copy);
            Assert.That(RateControl.ForScore(imported).Mirror, Is.EqualTo((MirrorAxes)axis));
            Assert.That(RateControl.ForScore(imported).Difficulty?.Values, Is.EqualTo(settings.Difficulty?.Values));
            Assert.That(RateControl.ForScore(imported).Speed, Is.EqualTo(export && rateMods == 0 ? 1.5 : 1.83));
            Assert.That(Score.ReplayData.Get(imported), Is.EqualTo(Score.ReplayData.Get(score)));
            var rateName = rateMods == 0 ? "" : rateMods == 64 ? "DT" : "NC";
            Assert.That(LocalScoreRateDisplay.Format(rateName.Length == 0 ? "MR" : rateName + ",MR", imported),
                Is.EqualTo((rateName.Length == 0 ? "" : rateName + "(1.83x),") + "MR(" + ((MirrorAxes)axis).ToString()[0] + ")" + (da ? ",DA(CS3.2,AR9.1)" : "")));
            Assert.That(DifficultyModHud.DisplayMods(imported) & MirrorSettings.Flag, Is.Not.Zero);
            Assert.That(BitConverter.ToInt32(data, 1) >= 30000001, Is.EqualTo(export));
        }
        var managerType = ApplyMirror.Target().DeclaringType!;
        var concrete = managerType.Assembly.GetTypes().First(t => !t.IsAbstract && managerType.IsAssignableFrom(t));
        var manager = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(concrete);
        managerType.GetFields(DifficultyControl.All).Single(f => f.FieldType == Score.EnabledMods.Reference.FieldType)
            .SetValue(manager, Score.EnabledMods.Get(score));
        var oldScore = Player.CurrentScore.Get();
        var oldMode = GameBase.Mode.Get();
        var oldRuleset = ChangeStandardRuleset.ModeField.GetValue(null);
        try
        {
            Player.CurrentScore.Set(score);
            GameBase.Mode.Set(Convert.ToInt32(Enum.Parse(GameBase.Mode.Reference.FieldType, "Play")));
            foreach (var mode in new[] { 0, 1, 2, 3 })
            {
                ChangeStandardRuleset.ModeField.SetValue(null, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, mode));
                var method = typeof(ApplyMirror).GetMethod("Initialize", DifficultyControl.All)!
                    .MakeGenericMethod(Osu.StablePlus.Stubs.XNA.Vector2.Class.Reference);
                object[] args = [NativeModMenu.Point(0, 0), -120f, 475f, manager];
                method.Invoke(null, args);
                Assert.That(NativeModDrawer.GetX(args[0]), Is.EqualTo(mode == 0 ? MirrorSettings.X(-120, (MirrorAxes)axis) : -120));
                Assert.That(NativeModDrawer.GetY(args[0]), Is.EqualTo(mode == 0 ? MirrorSettings.Y(475, (MirrorAxes)axis) : 475));
            }
        }
        finally
        {
            Player.CurrentScore.Set(oldScore); GameBase.Mode.Set(oldMode);
            ChangeStandardRuleset.ModeField.SetValue(null, oldRuleset);
            MirrorSettings.Selected = MirrorAxes.Horizontal;
        }
    }

    [TestCase(0, "H")]
    [TestCase(1, "V")]
    [TestCase(2, "B")]
    public void MirrorLabelPreferenceIsIndependentAndPersisted(int axes, string label)
    {
        var previous = ModLabelOptions.ShowMirror.Value;
        try
        {
            var settings = new RateSettings(mirror: (MirrorAxes)axes);
            var options = new ModLabelOptions();
            foreach (var enabled in new[] { true, false })
            {
                options.Load(new Settings { ShowMirrorInModLabels = enabled });
                Assert.That(ModLabelFormatter.Format("AP,MR", MirrorSettings.Flag, settings, null, false, false),
                    Is.EqualTo("AP,MR" + (enabled ? "(" + label + ")" : "")));
                Assert.That(ModLabelFormatter.Format("AutoPilot,Mirror", MirrorSettings.Flag, settings, null, true, true),
                    Is.EqualTo("AutoPilot, Mirror" + (enabled ? " (" + label + ")" : "")));
                var saved = new Settings(); options.Save(saved);
                Assert.That(saved.ShowMirrorInModLabels, Is.EqualTo(enabled));
            }
        }
        finally { ModLabelOptions.ShowMirror.Value = previous; }
    }

    [Test]
    public void FourthRowKeyboardAndCompatibilityAreStandardOnly()
    {
        var oldRuleset = ChangeStandardRuleset.ModeField.GetValue(null);
        var oldMods = ModManager.ModStatus.Get();
        var menu = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(NativeModMenu.Menu);
        var keyMethod = DifficultyModKeyboard.Target();
        var keyType = keyMethod.GetParameters()[0].ParameterType;
        var oldShift = DifficultyModKeyboard.Shift.GetValue(null);
        try
        {
            foreach (var mode in new[] { 0, 1, 2, 3 })
            {
                ChangeStandardRuleset.Target().Invoke(null, [Enum.ToObject(StandardModLayout.PlayMode.ReturnType, mode)]);
                ModManager.ModStatus.Set(MirrorSettings.Flag | DifficultyControl.Flag);
                AllowNoFailAssistance.Target().Invoke(null, [Enum.ToObject(Mods.Type.Reference, 0)]);
                Assert.That((ModManager.ModStatus.Get() & DifficultyControl.Flag) != 0, Is.EqualTo(mode == 0));
                Assert.That((ModManager.ModStatus.Get() & MirrorSettings.Flag) != 0, Is.EqualTo(mode == 0 || mode == 3));
                Assert.That(CustomRateOptions.ForMods(ModManager.ModStatus.Get()).Mirror.HasValue, Is.EqualTo(mode == 0));
                var y = NativeModDrawer.GetY(NativeModMenu.ModPosition.Invoke(menu, [2, 0]));
                Assert.That(y, Is.EqualTo(mode == 0 ? 90 : 120));
                DifficultyModKeyboard.Shift.SetValue(null, true);
                Assert.That(Convert.ToInt32(keyMethod.Invoke(null, [Enum.Parse(keyType, "Z")])) == DifficultyControl.Flag, Is.EqualTo(mode == 0));
                Assert.That(Convert.ToInt32(keyMethod.Invoke(null, [Enum.Parse(keyType, "X")])) == MirrorSettings.Flag, Is.EqualTo(mode == 0));
                DifficultyModKeyboard.Shift.SetValue(null, false);
                Assert.That(Convert.ToInt32(keyMethod.Invoke(null, [Enum.Parse(keyType, "Z")])) == DifficultyControl.Flag, Is.False);
                Assert.That(Convert.ToInt32(keyMethod.Invoke(null, [Enum.Parse(keyType, "X")])) == MirrorSettings.Flag, Is.False);
            }
            ChangeStandardRuleset.Target().Invoke(null, [Enum.ToObject(StandardModLayout.PlayMode.ReturnType, 0)]);
            Assert.That(ModManager.ModStatus.Get() & (MirrorSettings.Flag | DifficultyControl.Flag), Is.Zero);
            ModManager.ModStatus.Set(MirrorSettings.Flag | Mods.HardRock);
            AllowNoFailAssistance.Target().Invoke(null, [Enum.ToObject(Mods.Type.Reference, MirrorSettings.Flag)]);
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(MirrorSettings.Flag));
            ModManager.ModStatus.Set(MirrorSettings.Flag | Mods.HardRock);
            AllowNoFailAssistance.Target().Invoke(null, [Enum.ToObject(Mods.Type.Reference, Mods.HardRock)]);
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(Mods.HardRock));
        }
        finally
        {
            ChangeStandardRuleset.ModeField.SetValue(null, oldRuleset);
            ModManager.ModStatus.Set(oldMods);
            DifficultyModKeyboard.Shift.SetValue(null, oldShift);
        }
    }

    [TestCase(0)]
    [TestCase(64)]
    [TestCase(576)]
    public void DifficultyReplayImportsExportsAndKeepsItsOwnSettings(int mods)
    {
        var filename = Environment.GetEnvironmentVariable("OSU_TEST_DA_REPLAY");
        if (string.IsNullOrEmpty(filename)) Assert.Ignore("Set OSU_TEST_DA_REPLAY.");
        using var source = File.OpenRead(filename!);
        var score = ReadScore(source);
        var settings = RateControl.ForScore(score);
        Assert.That(settings.Speed, Is.EqualTo(2));
        Assert.That(settings.Difficulty!.Values, Is.EqualTo(new double?[] { null, 5, 7.5, 6.5 }));
        var wrapper = Score.EnabledMods.Reference.FieldType;
        var conversion = wrapper.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
        Score.EnabledMods.Set(score, conversion.Invoke(null, [Enum.ToObject(Mods.Type.Reference, mods)]));
        var previousScore = Player.CurrentScore.Get();
        var previousMode = GameBase.Mode.Get();
        try
        {
            DifficultyControl.Enabled = true;
            DifficultyControl.Selected = new DifficultySettings(1, 1, 1, 1);
            var playback = ((ICloneable)score).Clone();
            Player.CurrentScore.Set(playback);
            GameBase.Mode.Set(Convert.ToInt32(Enum.Parse(GameBase.Mode.Reference.FieldType, "Play")));
            var map = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Osu.StablePlus.Stubs.GameplayElements.Beatmaps.Beatmap.Class.Reference);
            for (var i = 0; i < 4; i++) DifficultyControl.Fields[i].SetValue(map, 9f);
            Assert.That(DifficultyControl.Read(map, 0), Is.EqualTo(9));
            Assert.That(DifficultyControl.Read(map, 1), Is.EqualTo(5));
            Assert.That(DifficultyControl.Read(map, 2), Is.EqualTo(7.5));
            Assert.That(DifficultyControl.Read(map, 3), Is.EqualTo(6.5));
            Assert.That(DifficultyControl.Fields.Select(f => f.GetValue(map)), Is.All.EqualTo(9f));
            foreach (var export in new[] { false, true })
            {
                using var bytes = new MemoryStream();
                using var writer = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, bytes)!;
                ExportLazerReplay.Write(playback, writer, export);
                var data = bytes.ToArray();
                using var copy = new MemoryStream(data);
                var reloaded = ReadScore(copy);
                Assert.That(RateControl.ForScore(reloaded).Difficulty!.Values, Is.EqualTo(settings.Difficulty.Values));
                Assert.That(Score.ReplayData.Get(reloaded), Is.EqualTo(Score.ReplayData.Get(score)));
                Directory.CreateDirectory(".scratch/exports");
                File.WriteAllBytes(".scratch/exports/DA-" + mods + (export ? "-lazer" : "-stable") + ".osr", data);
            }
        }
        finally
        {
            Player.CurrentScore.Set(previousScore);
            GameBase.Mode.Set(previousMode);
            DifficultyControl.Disable();
        }
    }

    [Test]
    public void DifficultyDefaultsFollowBeatmapAndResetIndividually()
    {
        var type = Osu.StablePlus.Stubs.GameplayElements.Beatmaps.Beatmap.Class.Reference;
        var a = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        var b = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        for (var i = 0; i < 4; i++) { DifficultyControl.Fields[i].SetValue(a, 9f); DifficultyControl.Fields[i].SetValue(b, 4f); }
        try
        {
            DifficultyControl.Disable();
            DifficultyControl.Set(2, 9.5);
            Assert.That(DifficultyControl.Value(a, 3), Is.EqualTo(9));
            Assert.That(DifficultyControl.Value(b, 3), Is.EqualTo(4));
            Assert.That(DifficultyControl.Value(b, 2), Is.EqualTo(9.5));
            DifficultyControl.Set(2, null);
            Assert.That(DifficultyControl.Value(b, 2), Is.EqualTo(4));
            DifficultyControl.Enabled = true;
            ResetDifficultyMod.Target().Invoke(null, null);
            Assert.That(DifficultyControl.Enabled, Is.False);
            Assert.That(ApplyDifficultyAdjust.Targets(), Does.Contain(DifficultyControl.Timing));
        }
        finally { DifficultyControl.Disable(); }
    }

    [TestCase(1.01)]
    [TestCase(1.25)]
    [TestCase(1.5)]
    [TestCase(1.7)]
    [TestCase(2)]
    public void NativeTimingConsumesDifficultyBeforeRate(double rate)
    {
        var managerType = DifficultyControl.Timing.DeclaringType!;
        var concrete = managerType.Assembly.GetTypes().First(t => !t.IsAbstract && managerType.IsAssignableFrom(t));
        var manager = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(concrete);
        var range = MethodReader.GetInstructions(DifficultyControl.Timing).Select(i => i.Operand).OfType<MethodInfo>()
            .Distinct().Single(m => !m.IsStatic && m.ReturnType == typeof(double) &&
                m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(double), typeof(double), typeof(double), typeof(double) }));
        var implementation = managerType.GetMethods(DifficultyControl.All).Single(m => m.GetBaseDefinition() == range.GetBaseDefinition());
        var modsField = MethodReader.GetInstructions(implementation).Where(i => i.Opcode == Ldfld)
            .Select(i => i.Operand).OfType<FieldInfo>().First();
        var wrap = modsField.FieldType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == modsField.FieldType);
        modsField.SetValue(manager, wrap.Invoke(null, [Enum.ToObject(Mods.Type.Reference, 0)]));
        var preempt = (double)range.Invoke(manager, [9.5d, 1800d, 1200d, 450d]) / rate;
        var window = (double)range.Invoke(manager, [9d, 80d, 50d, 20d]) / rate;
        Assert.That(preempt, Is.EqualTo(525 / rate).Within(0.00001));
        Assert.That(window, Is.EqualTo(26 / rate).Within(0.00001));
        Assert.That(DifficultySettings.Effective(2, 9.5, rate), Is.EqualTo(5 + (1200 - preempt) / 150).Within(0.00001));
        Assert.That(DifficultySettings.Effective(3, 9, rate), Is.EqualTo((80 - window) / 6).Within(0.00001));
    }

    [Test]
    public void NewPlayCapturesDifficultyAndLaterMenuChangesCannotReplaceIt()
    {
        try
        {
            DifficultyControl.Enabled = true;
            DifficultyControl.Selected = new DifficultySettings(4, 5, 9.5, 9);
            var score = Activator.CreateInstance(Score.Class.Reference, true)!;
            RateControl.CaptureGameplayScore(score, null);
            DifficultyControl.Set(2, 1);
            DifficultyControl.Disable();
            Assert.That(RateControl.ForScore(score).Difficulty!.Values, Is.EqualTo(new double?[] { 4, 5, 9.5, 9 }));
            var legacy = Activator.CreateInstance(Score.Class.Reference, true)!;
            Assert.That(RateControl.ForScore(legacy).Difficulty, Is.Null);
        }
        finally { DifficultyControl.Disable(); }
    }

    [Test]
    public void DifficultyIconAndResetResourcesAreEmbedded()
    {
        foreach (var name in new[] { "selection-mod-difficultyadjust.png", "setting-reset.png" })
        {
            using var stream = typeof(RateSettings).Assembly.GetManifestResourceStream("Osu.StablePlus.Hook.Resources." + name);
            Assert.That(stream, Is.Not.Null);
            Assert.That(stream!.ReadByte(), Is.EqualTo(137));
        }
        Assert.That(NativeModMenu.ModPosition.ReturnType.FullName, Is.EqualTo("Microsoft.Xna.Framework.Vector2"));
    }

    [Test]
    public void NativeModKeyboardAndCompatibility()
    {
        var keyMethod = DifficultyModKeyboard.Target();
        var key = Enum.Parse(keyMethod.GetParameters()[0].ParameterType, "Z");
        var oldShift = DifficultyModKeyboard.Shift.GetValue(null);
        try
        {
            DifficultyModKeyboard.Shift.SetValue(null, true);
            Assert.That(Convert.ToInt32(keyMethod.Invoke(null, [key])), Is.EqualTo(DifficultyControl.Flag));
        }
        finally { DifficultyModKeyboard.Shift.SetValue(null, oldShift); }
        var previous = ModManager.ModStatus.Get();
        try
        {
            ModManager.ModStatus.Set(Mods.HardRock | Mods.DoubleTime | DifficultyControl.Flag);
            AllowNoFailAssistance.Target().Invoke(null, [Enum.ToObject(Mods.Type.Reference, DifficultyControl.Flag)]);
            Assert.That(DifficultyControl.Enabled, Is.True);
            Assert.That(ModManager.ModStatus.Get() & Mods.HardRock, Is.Zero);
            Assert.That(ModManager.ModStatus.Get() & Mods.DoubleTime, Is.Not.Zero);
            ModManager.ModStatus.Set(ModManager.ModStatus.Get() | Mods.Easy);
            AllowNoFailAssistance.Target().Invoke(null, [Enum.ToObject(Mods.Type.Reference, Mods.Easy)]);
            Assert.That(DifficultyControl.Enabled, Is.False);
        }
        finally { ModManager.ModStatus.Set(previous); }
    }

    [TestCase(0)]
    [TestCase(128)]
    [TestCase(8192)]
    public void AssistedMissSoundKeepsTheCorrectComboGate(int mods)
    {
        var previous = ModManager.ModStatus.Get();
        try
        {
            ModManager.ModStatus.Set(mods);
            var original = PatchProcessor.GetOriginalInstructions(AllowRelaxComboBreakSound.Target());
            var patched = AllowRelaxComboBreakSound.Transpiler(original.Select(i => new CodeInstruction(i))).ToList();
            var index = Enumerable.Range(1, patched.Count - 2).Single(i =>
                patched[i].opcode == Ldc_I4_S && Equals(patched[i].operand, (sbyte)20) &&
                patched[i - 1].opcode == Callvirt && patched[i + 1].opcode == Ble_S);
            Assert.That(index, Is.GreaterThan(0));
            Assert.That(patched[index - 1].opcode, Is.EqualTo(Callvirt));
            Assert.That(patched[index + 1].opcode, Is.EqualTo(Ble_S));
            Assert.That(patched.Skip(index + 2).Take(4).Select(i => i.opcode), Is.All.EqualTo(Nop));
        }
        finally { ModManager.ModStatus.Set(previous); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExportIsLazerReadableWhileInternalSaveRetainsStableVersion(bool nightcore)
    {
        using var source = OpenUserReplay();
        var score = ReadScore(source);
        if (nightcore)
        {
            var wrapper = Score.EnabledMods.Reference.FieldType;
            var conversion = wrapper.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
            Score.EnabledMods.Set(score, conversion.Invoke(null, [Enum.ToObject(Mods.Type.Reference, 200 | Mods.Nightcore)]));
        }
        RateControl.Remember(score, new RateSettings(1.95, true));
        var writerType = Score.WriteReplay.Reference.GetParameters()[0].ParameterType;
        foreach (var export in new[] { false, true })
        {
            using var bytes = new MemoryStream();
            using var writer = (BinaryWriter)Activator.CreateInstance(writerType, bytes)!;
            ExportLazerReplay.Write(score, writer, export);
            var data = bytes.ToArray();
            Assert.That(BitConverter.ToInt32(data, 1) >= 30000001, Is.EqualTo(export));
            using var copy = new MemoryStream(data);
            var imported = ReadScore(copy);
            Assert.That(RateControl.ForScore(imported).Speed, Is.EqualTo(1.95));
            Assert.That(RateControl.ForScore(imported).AdjustPitch, Is.True);
            Assert.That(LocalScoreRateDisplay.Format(nightcore ? "HD,NC,RX" : "HD,DT,RX", imported),
                Is.EqualTo(nightcore ? "HD,NC(1.95x),RX" : "HD,DT(1.95x),RX"));
            Assert.That(Score.ReplayData.Get(imported), Is.EqualTo(Score.ReplayData.Get(score)));
        }
    }

    [Test]
    public void EmbeddedDatabaseScoresHaveNoStandaloneReplayTrailer()
    {
        using var source = OpenUserReplay();
        var score = ReadScore(source);
        var writerType = Score.WriteReplay.Reference.GetParameters()[0].ParameterType;
        using var standalone = new MemoryStream();
        using var standaloneWriter = (BinaryWriter)Activator.CreateInstance(writerType, standalone)!;
        Score.WriteReplay.Invoke(score, [standaloneWriter]);
        using var embedded = new MemoryStream();
        using var embeddedWriter = (BinaryWriter)Activator.CreateInstance(writerType, embedded)!;
        embedded.Write(new byte[4], 0, 4); // database header precedes its score records
        Score.WriteReplay.Invoke(score, [embeddedWriter]);
        Assert.That(System.Text.Encoding.ASCII.GetString(embedded.ToArray()), Does.Not.Contain("osu!patcher-DT"));
        Assert.That(System.Text.Encoding.ASCII.GetString(standalone.ToArray()), Does.Contain("osu!patcher-DT"));
    }

    [Test]
    public void DtNcSwitchPreservesRateButDisablingResetsIt()
    {
        var previous = ModManager.ModStatus.Get();
        try
        {
            new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.8, DoubleTimeAdjustPitch = false });
            CustomRateOptions.ObserveMods(Mods.DoubleTime);
            CustomRateOptions.ObserveMods(Mods.DoubleTime | Mods.Nightcore);
            ModManager.ModStatus.Set(Mods.DoubleTime | Mods.Nightcore);
            var score = Activator.CreateInstance(Score.Class.Reference, true)!;
            RateControl.CaptureGameplayScore(score, null);
            Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(1.8));
            Assert.That(RateControl.ForScore(score).AdjustPitch, Is.True);
            CustomRateOptions.ObserveMods(Mods.DoubleTime);
            Assert.That(CustomRateOptions.Selected.Speed, Is.EqualTo(1.8));
            Assert.That(CustomRateOptions.ForMods(Mods.DoubleTime).AdjustPitch, Is.False);
            CustomRateOptions.ObserveMods(Mods.Hidden);
            CustomRateOptions.ObserveMods(Mods.DoubleTime | Mods.Nightcore);
            Assert.That(CustomRateOptions.Selected.Speed, Is.EqualTo(1.5));
            Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(1.8));
        }
        finally
        {
            ModManager.ModStatus.Set(previous);
            new CustomRateOptions().Load(new Settings());
        }
    }

    [TestCase(Mods.Relax, Mods.NoFail)]
    [TestCase(Mods.NoFail, Mods.Relax)]
    [TestCase(Mods.Relax2, Mods.NoFail)]
    [TestCase(Mods.NoFail, Mods.Relax2)]
    [TestCase(Mods.NoFail | Mods.Relax, Mods.None)]
    [TestCase(Mods.NoFail | Mods.Relax2, Mods.None)]
    public void NoFailWorksWithAssistanceInEitherSelectionOrder(int existing, int added)
    {
        var previous = ModManager.ModStatus.Get();
        try
        {
            var mods = existing | added | Mods.Hidden | Mods.DoubleTime;
            ModManager.ModStatus.Set(mods);
            AllowNoFailAssistance.Target().Invoke(null, [Enum.ToObject(Mods.Type.Reference, added)]);
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(mods));
        }
        finally { ModManager.ModStatus.Set(previous); }
    }

    [TestCase(Mods.NoFail | Mods.SuddenDeath | Mods.Perfect, Mods.NoFail, Mods.NoFail)]
    [TestCase(Mods.NoFail | Mods.SuddenDeath, Mods.SuddenDeath, Mods.SuddenDeath)]
    [TestCase(Mods.Relax | Mods.Relax2 | Mods.NoFail, Mods.Relax, Mods.Relax | Mods.NoFail)]
    [TestCase(Mods.Relax | Mods.Relax2 | Mods.NoFail, Mods.Relax2, Mods.Relax2 | Mods.NoFail)]
    public void OtherIncompatibilitiesRemain(int mods, int changed, int expected)
    {
        var previous = ModManager.ModStatus.Get();
        try
        {
            ModManager.ModStatus.Set(mods);
            AllowNoFailAssistance.Target().Invoke(null, [Enum.ToObject(Mods.Type.Reference, changed)]);
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(expected));
        }
        finally { ModManager.ModStatus.Set(previous); }
    }

    [TestCase(Mods.DoubleTime, "DoubleTime", 1.7, "DoubleTime(1.7x)")]
    [TestCase(Mods.DoubleTime | Mods.Nightcore, "Nightcore", 1.95, "Nightcore(1.95x)")]
    [TestCase(Mods.DoubleTime, "DoubleTime", 1.5, "DoubleTime")]
    [TestCase(Mods.None, "", 1.7, "")]
    public void LargeModLabelOnlyAddsCustomRate(int mods, string label, double rate, string expected) =>
        Assert.That(SelectedRateDisplay.Format(label, mods, rate), Is.EqualTo(expected));

    private static Stream OpenUserReplay()
    {
        var path = Environment.GetEnvironmentVariable("OSU_TEST_REPLAY");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set OSU_TEST_REPLAY to the supplied DT 1.7x replay.");
        return File.OpenRead(path!);
    }
}
