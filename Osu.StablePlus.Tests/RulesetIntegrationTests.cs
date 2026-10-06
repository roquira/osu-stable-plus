using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Tests;

public partial class StableIntegrationTests
{
    [TestCase(1, "OD:8", "OD:10.89")]
    [TestCase(2, "AR:9 OD:8", "AR:10.33 OD:8")]
    [TestCase(3, "OD:8", "OD:8")]
    public void DifficultyDescriptionsUseModeWindows(int mode, string input, string expected) =>
        Assert.That(SelectionDetails.FormatOther(input, mode, 1.5, "2", "400"), Is.EqualTo(expected));

    private static bool ScrollTestNotReplay(ref bool __result) { __result = false; return false; }

    [TestCase(false, 29)]
    [TestCase(false, 32)]
    [TestCase(true, 29)]
    [TestCase(true, 32)]
    public void ManiaScrollHonoursSpeedPreferenceAtRecordedRate(bool bpmScaled, int speed)
    {
        var setter = ModeRateTiming.ManiaScroll;
        var type = setter.DeclaringType!;
        var il = MethodReader.GetInstructions(setter).ToArray();
        var fields = il.Select(i => i.Operand).OfType<FieldInfo>().Distinct().ToArray();
        var referenceBpm = il.Where(i => i.Opcode == Ldsfld).Select(i => i.Operand).OfType<FieldInfo>()
            .First(f => f.FieldType == typeof(double));
        var booleanWrapper = il.Select(i => i.Operand).OfType<MethodInfo>()
            .First(m => m.Name == "op_Implicit" && m.ReturnType == typeof(bool)).GetParameters()[0].ParameterType;
        var preference = fields.First(f => f.IsStatic && f.FieldType == booleanWrapper);
        var changed = fields.Single(f => f.IsStatic && f.FieldType == typeof(bool) && f.DeclaringType == type);
        var distance = type.GetMethods(DifficultyControl.All).Single(m => m.IsStatic && m.ReturnType == typeof(double) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(double), typeof(double) }) &&
            MethodReader.GetInstructions(m).Count(i => i.Opcode == Div) == 1);
        var duration = type.GetMethods(DifficultyControl.All).Single(m => m.IsStatic && m.ReturnType == typeof(double) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(double), typeof(double) }) &&
            MethodReader.GetInstructions(m).Count(i => i.Opcode == Div) == 2);
        var saved = type.GetFields(DifficultyControl.All).Where(f => f.IsStatic).ToDictionary(f => f, f => f.GetValue(null));
        var oldPreference = preference.GetValue(null);
        var oldScore = Player.CurrentScore.Get(); var oldMode = GameBase.Mode.Get(); var oldMods = ModManager.ModStatus.Get();
        var oldRate = CustomRateOptions.Selected; var oldHt = CustomRateOptions.SelectedHalfTime;
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.ScoreMode.FieldType, 3));
        harmony.Patch(Player.IsReplay.Reference, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(ScrollTestNotReplay)));
        try
        {
            GameBase.Mode.Set(Convert.ToInt32(Enum.Parse(GameBase.Mode.Reference.FieldType, "Play")));
            Player.CurrentScore.Set(score);
            preference.SetValue(null, new Osu.StablePlus.Stubs.Wrappers.BindableWrapper<bool>(Osu.StablePlus.Stubs.Wrappers.BindableType.Bool, bpmScaled, false).Bindable);
            referenceBpm.SetValue(null, 200d);
            changed.SetValue(null, false);
            // A conflicting menu rate must not override the captured play/replay rate.
            new CustomRateOptions().Load(new Settings { DoubleTimeSpeed = 1.9, HalfTimeSpeed = .6 });
            foreach (int mods in new[] { 0, Mods.DoubleTime, Mods.DoubleTime | Mods.Nightcore, Mods.HalfTime })
                foreach (double rate in mods == 0 ? new[] { 1d } : mods == Mods.HalfTime ? new[] { .5, .75, .99 } : new[] { 1.01, 1.15, 1.25, 1.5, 1.7, 2d })
                {
                    SetRulesetScoreMods(score, mods); ModManager.ModStatus.Set(mods);
                    RateControl.Remember(score, new RateSettings(mods == 0 ? 1.5 : rate));
                    setter.Invoke(null, new[] { (object)(double)speed, false, Enum.ToObject(setter.GetParameters()[2].ParameterType, 0) });
                    // Exercise stable's actual note/hold displacement calculation: one
                    // real second covers rate * 1000 milliseconds of beatmap time.
                    var actual = (double)distance.Invoke(null, new object[] { 300d, 1000 * rate });
                    var expected = bpmScaled ? 21d * speed * 1000 * rate / 300 : 35d * speed;
                    Assert.That(actual, Is.EqualTo(expected).Within(1e-8), $"Mods {mods}, rate {rate}");
                    Assert.That((double)distance.Invoke(null, new object[] { 300d, 0d }), Is.Zero,
                        "Notes still reach the hit position at their original map timestamp.");
                    Assert.That((double)duration.Invoke(null, new object[] { 300d, actual }) / rate,
                        Is.EqualTo(1000).Within(1e-8), "Note travel and visibility lead-in use the same clock.");
                    if (mods != 0) Assert.That(RateControl.GameplayPercent(), Is.EqualTo(rate * 100).Within(1e-8));
                }
        }
        finally
        {
            harmony.Unpatch(Player.IsReplay.Reference, HarmonyPatchType.All, harmony.Id);
            foreach (var pair in saved) pair.Key.SetValue(null, pair.Value);
            preference.SetValue(null, oldPreference);
            Player.CurrentScore.Set(oldScore); GameBase.Mode.Set(oldMode); ModManager.ModStatus.Set(oldMods);
            new CustomRateOptions().Load(new Settings
            {
                DoubleTimeSpeed = oldRate.Speed,
                DoubleTimeAdjustPitch = oldRate.AdjustPitch,
                HalfTimeSpeed = oldHt.Speed,
                HalfTimeAdjustPitch = oldHt.AdjustPitch
            });
        }
    }

    [TestCase(64, 1.01)]
    [TestCase(64, 1.25)]
    [TestCase(64, 1.5)]
    [TestCase(576, 1.7)]
    [TestCase(64, 2)]
    [TestCase(256, .5)]
    [TestCase(256, .75)]
    [TestCase(256, .99)]
    public void ManiaNativeWindowsCompensateForRecordedClock(int mods, double rate)
    {
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.ScoreMode.FieldType, 3));
        SetRulesetScoreMods(score, mods);
        RateControl.Remember(score, new RateSettings(rate));
        var oldScore = Player.CurrentScore.Get(); var oldMode = GameBase.Mode.Get();
        var window = ModeRateTiming.ManiaWindow;
        var manager = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(window.DeclaringType!);
        GC.SuppressFinalize(manager);
        var field = MethodReader.GetInstructions(window).Where(i => i.Opcode == Ldfld)
            .Select(i => i.Operand).OfType<FieldInfo>().Distinct().Single();
        var wrap = field.FieldType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == "op_Implicit" && m.ReturnType == field.FieldType);
        field.SetValue(manager, wrap.Invoke(null, new[] { Enum.ToObject(Mods.Type.Reference, mods) }));
        try
        {
            GameBase.Mode.Set(Convert.ToInt32(Enum.Parse(GameBase.Mode.Reference.FieldType, "Play")));
            Player.CurrentScore.Set(score);
            foreach (double nominal in new[] { 16d, 34d, 67d, 97d, 121d, 158d })
            {
                int actual = (int)window.Invoke(manager, new object[] { nominal });
                Assert.That(actual, Is.EqualTo((int)(nominal * rate)));
                Assert.That(actual / rate, Is.EqualTo(nominal).Within(1 / rate));
            }
        }
        finally { GameBase.Mode.Set(oldMode); Player.CurrentScore.Set(oldScore); }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void OtherModesScaleOnlySavedScoreV1Multiplier(int mode)
    {
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.ScoreMode.FieldType, mode));
        var type = Osu.StablePlus.Stubs.GameModes.Play.Rulesets.Ruleset.Class.Reference.Assembly.GetTypes().First(t => !t.IsAbstract &&
            t.IsSubclassOf(Osu.StablePlus.Stubs.GameModes.Play.Rulesets.Ruleset.Class.Reference));
        var ruleset = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(ruleset);
        Osu.StablePlus.Stubs.GameModes.Play.Rulesets.Ruleset.CurrentScore.Set(ruleset, score);
        Assert.That(NativeStableScoring.Supports(mode), Is.True);
        foreach (int flags in new[] { Mods.DoubleTime, Mods.DoubleTime | Mods.Nightcore, Mods.HalfTime })
            foreach (double rate in flags == Mods.HalfTime ? new[] { .5, .75, .99 } : new[] { 1.01, 1.25, 1.5, 1.7, 2 })
            {
                SetRulesetScoreMods(score, flags);
                RateControl.Remember(score, new RateSettings(rate, scoring: new StableScoringSettings(2)));
                Assert.That(NativeStableScoring.ScaleModMultiplier(1.234, ruleset),
                    Is.EqualTo(1.234 * StableScoreMath.RelativeRate(flags, rate, mode)).Within(1e-12));
                Assert.That(NativeStableScoring.AdjustAward(300, ruleset), Is.EqualTo(300));
                SetRulesetScoreMods(score, flags | StableScoreMath.ScoreV2);
                Assert.That(NativeStableScoring.ScaleModMultiplier(1.234, ruleset), Is.EqualTo(1.234));
                SetRulesetScoreMods(score, flags);
                RateControl.Remember(score, new RateSettings(rate, scoring: new StableScoringSettings(0)));
                Assert.That(NativeStableScoring.ScaleModMultiplier(1.234, ruleset), Is.EqualTo(1.234));
            }
    }

    [Test]
    public void ModeRateBindingsResolveVerifiedClockOperations()
    {
        Assert.That(ModeRateTiming.Helpers.Length, Is.EqualTo(2));
        TestContext.WriteLine("Mania window: " + ModeRateTiming.ManiaWindow);
        TestContext.WriteLine("Catch initialization: " + ModeRateTiming.CatchInitialize);
        foreach (var method in ModeRateCalls.Targets()) TestContext.WriteLine("Clock consumer: " + method.DeclaringType + " " + method);
        Assert.That(ModeRateTiming.ManiaWindow.ReturnType, Is.EqualTo(typeof(int)));
        Assert.That(ModeRateCalls.Targets(), Is.Not.Empty);
        Assert.That(Enumerable.Range(0, 4).All(ModeRateTiming.Supports), Is.True);
        Assert.That(MirrorDropdown.OpensUp.FieldType, Is.EqualTo(typeof(bool)));
        Assert.That(Osu.StablePlus.Hook.Patches.LivePerformance.TrackOtherModeProgress.AudioTime.FieldType, Is.EqualTo(typeof(int)));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void NativeConversionStructureIsCheckedByOfficialEngine(int mode)
    {
        // Circle-only input avoids native slider render-target allocation in
        // this headless test process. Engine tests cover slider conversions.
        string? original = Environment.GetEnvironmentVariable("OSU_TEST_MAP");

        if (string.IsNullOrEmpty(original))
            Assert.Ignore("Set OSU_TEST_MAP to a standard .osu file to run conversion structure checks.");

        string fixture = Path.Combine(Environment.CurrentDirectory, ".scratch", "conversion-circles.osu");
        var lines = File.ReadAllLines(original!);
        bool objects = false;
        File.WriteAllLines(fixture, lines.Where(line =>
        {
            if (line == "[HitObjects]") { objects = true; return true; }
            if (!objects || string.IsNullOrWhiteSpace(line)) return true;
            var parts = line.Split(',');
            return parts.Length >= 4 && (int.Parse(parts[3]) & 1) != 0;
        }));
        var map = CreateSelectionTestMap(fixture);
        var structure = Osu.StablePlus.Hook.Patches.LivePerformance.NativeObjectStructure.Capture(map, mode, 0);
        Assert.That(structure, Is.Not.Empty);
        TestContext.WriteLine($"Mode {mode}, stable objects: {structure.Length}; first: {string.Join(",", structure[0])}");
        var request = new Osu.StablePlus.Performance.CalculationRequest
        {
            MapPath = fixture,
            Mode = mode,
            SourceMode = 0,
            StarsOnly = true,
            ObjectStructure = structure
        };
        var result = Osu.StablePlus.Performance.PerformanceClient.CalculateAsync(request).GetAwaiter().GetResult();
        TestContext.WriteLine("Conversion check: " + (result.Error ?? "compatible"));
        Assert.That(result.Error, Is.Null);
        // Changed timing must not be silently accepted, even when counts match.
        request.ObjectStructure = structure.Select(o => o.ToArray()).ToArray();
        request.ObjectStructure[0][0] -= 100;
        var rejected = Osu.StablePlus.Performance.PerformanceClient.CalculateAsync(request).GetAwaiter().GetResult();
        Assert.That(rejected.Error, Does.Contain("converted object"));
    }
    [TestCase(DifficultyControl.Flag, "Override a beatmap's difficulty settings.")]
    [TestCase(MirrorSettings.Flag, "Flip objects on the chosen axes.")]
    public void ModTooltipsUseSpriteListRatherThanModFlagList(int flag, string description)
    {
        // Match the native mod control's real field layout without requiring a
        // graphics context: it contains both List<sprite> and List<Mods>.
        var control = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(NativeModMenu.ModIcon.DeclaringType!);
        GC.SuppressFinalize(control);
        var sprite = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(NativeModMenu.Sprite.DeclaringType!);
        GC.SuppressFinalize(sprite);
        var lists = control.GetType().GetFields(NativeModMenu.Members).Where(f => f.FieldType.IsGenericType &&
            f.FieldType.GetGenericTypeDefinition() == typeof(List<>)).ToArray();
        Assert.That(lists.Length, Is.GreaterThan(1), "Exercise the ambiguity that crashed mod-menu construction.");
        foreach (var field in lists)
        {
            var list = (System.Collections.IList)Activator.CreateInstance(field.FieldType)!;
            var element = field.FieldType.GetGenericArguments()[0];
            list.Add(element.IsEnum ? Enum.ToObject(element, flag) : sprite);
            field.SetValue(control, list);
        }
        Assert.That(NativeModMenu.GetSprites(control).ToArray(), Is.EqualTo(new[] { sprite }));
        NativeModMenu.SetModTooltip(control, flag);
        Assert.That(NativeModMenu.ModTooltip.GetValue(sprite), Is.EqualTo(description));
    }

    private static object? selectedRulesetTestMap;
    private static bool SelectedRulesetMap(ref object? __result) { __result = selectedRulesetTestMap; return false; }

    [TestCase("4K Normal", 2)]
    [TestCase("4K Hard", 3)]
    public void NativeManiaStarsAreUsedEvenWithStandardSelected(string difficulty, double upperBound)
    {
        var directory = Environment.GetEnvironmentVariable("OSU_TEST_MANIA_SET");
        if (string.IsNullOrEmpty(directory)) Assert.Ignore("Set OSU_TEST_MANIA_SET to the Din Don Dan set.");
        var path = Directory.GetFiles(directory!, "*.osu").Single(p => p.EndsWith("[" + difficulty + "].osu"));
        var map = CreateSelectionTestMap(path);
        var oldMode = ChangeStandardRuleset.ModeField.GetValue(null);
        var oldMods = ModManager.ModStatus.Get();
        try
        {
            ChangeStandardRuleset.ModeField.SetValue(null, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, 0));
            ModManager.ModStatus.Set(0);
            Assert.That(StandardModLayout.NativeModeOf(map), Is.EqualTo(3));
            Assert.That(StandardModLayout.ModeFor(map), Is.EqualTo(3));
            var factory = map.GetType().GetMethods(DifficultyControl.All).Single(m =>
                m.ReturnType == SelectionStars.CalculateMethod.DeclaringType && m.GetParameters().Length == 1);
            var calculator = factory.Invoke(map, [Enum.ToObject(StandardModLayout.PlayMode.ReturnType, 3)]);
            try
            {
                var stars = (double)SelectionStars.CalculateMethod.Invoke(calculator, [Enum.ToObject(Mods.Type.Reference, 0), null, null]);
                TestContext.WriteLine($"Din Don Dan {difficulty}: native mania {stars:0.######} stars");
                Assert.That(stars, Is.GreaterThan(0).And.LessThan(upperBound));
                var caches = map.GetType().GetFields(DifficultyControl.All).Single(f => f.FieldType == typeof(Dictionary<int, float>[]));
                ((Dictionary<int, float>[])caches.GetValue(map))[3][0] = (float)stars;
                var expected = (string)SelectionDetails.Description.Invoke(map, null);
                TestContext.WriteLine(expected);
                var official = Osu.StablePlus.Performance.PerformanceClient.CalculateAsync(new Osu.StablePlus.Performance.CalculationRequest
                {
                    MapPath = path,
                    Mode = 3,
                    SourceMode = 3,
                    StarsOnly = true
                }).GetAwaiter().GetResult();
                Assert.That(official.Error, Is.Null);
                Assert.That(official.Stars, Is.GreaterThan(0).And.LessThan(upperBound));
                Assert.That(SelectionDetails.Describe(map), Does.StartWith(expected.Substring(0, expected.IndexOf("Star Rating:", StringComparison.Ordinal))));
                Assert.Throws<InvalidOperationException>(() => SelectionStars.Calculate(map, 0, 1, null));
            }
            finally
            {
                SelectionStars.CalculateMethod.DeclaringType!.GetMethods(DifficultyControl.All).Single(m =>
                    m.IsPublic && m.IsVirtual && m.ReturnType == typeof(void) && m.GetParameters().Length == 0).Invoke(calculator, null);
            }
        }
        finally { ChangeStandardRuleset.ModeField.SetValue(null, oldMode); ModManager.ModStatus.Set(oldMods); }
    }

    [Test]
    public void NativeAndConvertedRulesetsCarryRatesWithoutStandardOnlyMods()
    {
        var map = CreateSelectionTestMap();
        var nativeMode = MethodReader.GetInstructions(StandardModLayout.NativeBeatmapMode)
            .Select(i => i.Operand).OfType<FieldInfo>().Single();
        var oldMode = ChangeStandardRuleset.ModeField.GetValue(null);
        var oldMods = ModManager.ModStatus.Get();
        var oldScore = Player.CurrentScore.Get();
        var oldDifficulty = DifficultyControl.Selected;
        var preferences = new Settings(); new CustomRateOptions().Save(preferences);
        selectedRulesetTestMap = map;
        harmony.Patch(DifficultyControl.CurrentBeatmap, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(SelectedRulesetMap)));
        try
        {
            new CustomRateOptions().Load(new Settings
            {
                DoubleTimeSpeed = 1.7,
                DoubleTimeAdjustPitch = true,
                HalfTimeSpeed = 0.6,
                HalfTimeAdjustPitch = true
            });
            DifficultyControl.Selected = new DifficultySettings(0, 0, 10, 10);
            foreach (var selected in new[] { 0, 1, 2, 3 })
                foreach (var native in new[] { 0, 1, 2, 3 })
                {
                    ChangeStandardRuleset.ModeField.SetValue(null, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, selected));
                    nativeMode.SetValue(map, Enum.ToObject(nativeMode.FieldType, native));
                    var effective = native == 0 ? selected : native;
                    Assert.That(StandardModLayout.IsStandard, Is.EqualTo(effective == 0));
                    foreach (var rateMod in new[] { Mods.DoubleTime, Mods.DoubleTime | Mods.Nightcore, Mods.HalfTime })
                    {
                        var mods = rateMod | DifficultyControl.Flag | MirrorSettings.Flag;
                        ModManager.ModStatus.Set(mods);
                        var settings = CustomRateOptions.ForMods(mods);
                        var expected = rateMod == Mods.HalfTime ? 0.6 : 1.7;
                        Assert.That(settings.Speed, Is.EqualTo(expected), $"Selected {selected}, native {native}, mods {rateMod}");
                        Assert.That(settings.Difficulty != null, Is.EqualTo(effective == 0));
                        Assert.That(settings.Mirror != null, Is.EqualTo(effective == 0));
                        Assert.That(settings.AdjustPitch, Is.True);
                        Assert.That(SelectionDetails.ClockRate, Is.EqualTo(expected));
                        if (effective != 0)
                        {
                            Assert.That(SelectionDetails.Rate(1.5f), Is.EqualTo((float)expected));
                            var score = Activator.CreateInstance(Score.Class.Reference, true)!;
                            StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, effective));
                            SetRulesetScoreMods(score, rateMod);
                            RateControl.CaptureGameplayScore(score, null);
                            Player.CurrentScore.Set(score);
                            Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(expected));
                            Assert.That(RateControl.ForScore(score).Scoring!.Version, Is.EqualTo(2));
                            // A replay snapshot takes precedence over the menu.
                            RateControl.Remember(score, new RateSettings(rateMod == Mods.HalfTime ? 0.5 : 2));
                            Assert.That(RateControl.GameplayMultiplier(), Is.EqualTo(rateMod == Mods.HalfTime ? .5 : 2));
                            Assert.That(RateControl.GameplayPercent(), Is.EqualTo(rateMod == Mods.HalfTime ? 50 : 200));
                        }
                    }
                }
        }
        finally
        {
            harmony.Unpatch(DifficultyControl.CurrentBeatmap, HarmonyPatchType.All, harmony.Id);
            selectedRulesetTestMap = null;
            ChangeStandardRuleset.ModeField.SetValue(null, oldMode); ModManager.ModStatus.Set(oldMods);
            Player.CurrentScore.Set(oldScore); DifficultyControl.Selected = oldDifficulty;
            new CustomRateOptions().Load(preferences);
        }
    }

    private static void SetRulesetScoreMods(object score, int mods)
    {
        var wrapper = Score.EnabledMods.Reference.FieldType;
        var conversion = wrapper.GetMethods().Single(m => m.Name == "op_Implicit" && m.ReturnType == wrapper);
        Score.EnabledMods.Set(score, conversion.Invoke(null, [Enum.ToObject(Mods.Type.Reference, mods)]));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void UnsupportedReplaySettingsAreDetectedWithoutChangingScores(int mode)
    {
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, mode));
        foreach (var mods in new[] { 0, Mods.DoubleTime, Mods.DoubleTime | Mods.Nightcore, Mods.HalfTime, MirrorSettings.Flag })
        {
            SetRulesetScoreMods(score, mods);
            RateControl.Remember(score, CustomRateOptions.NativeForMods(mods));
            Assert.That(RulesetCompatibility.UnsupportedReplay(score), Is.False);
        }
        SetRulesetScoreMods(score, Mods.DoubleTime);
        RateControl.Remember(score, new RateSettings(1.7));
        Assert.That(RulesetCompatibility.UnsupportedReplay(score), Is.False);
        Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(1.7));
        var da = ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"DA\",\"settings\":{\"overall_difficulty\":8}}]}", 0, mode)!;
        RateControl.Remember(score, da);
        Assert.That(RulesetCompatibility.UnsupportedReplay(score), Is.True);
        Assert.That(RateControl.GetMods(score), Is.EqualTo(Mods.DoubleTime), "Reading metadata must not add standard-only flags.");
        var mirror = ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"MR\"}]}", MirrorSettings.Flag, mode);
        Assert.That(mirror?.UnsupportedRulesetSettings ?? false, Is.EqualTo(mode != 3));
        Assert.That(RulesetCompatibility.ExitPlayer, Is.Not.Null);
        Assert.That(NativeModMenu.ModTooltip.FieldType, Is.EqualTo(typeof(string)));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void NonStandardReplayRoundTripsRatesWithoutChangingFrames(int mode)
    {
        using var source = OpenUserReplay();
        var score = ReadScore(source);
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, mode));
        var mods = Mods.DoubleTime | (mode == 3 ? MirrorSettings.Flag : 0);
        SetRulesetScoreMods(score, mods);
        RateControl.Remember(score, new RateSettings(1.7, true, scoring: new StableScoringSettings(2)));
        foreach (var export in new[] { false, true })
        {
            using var bytes = new MemoryStream();
            using var writer = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, bytes)!;
            ExportLazerReplay.Write(score, writer, export);
            bytes.Position = 0;
            var settings = ReplayRateMetadata.ReadReplay(bytes)!;
            Assert.That(settings.Speed, Is.EqualTo(1.7));
            Assert.That(settings.Scoring!.Version, Is.EqualTo(2));
            Assert.That(settings.Mirror, Is.Null);
            Assert.That(BitConverter.ToInt32(bytes.ToArray(), 1) >= 30000000, Is.EqualTo(export));
            bytes.Position = 0;
            var copy = ReadScore(bytes);
            Assert.That(StandardModLayout.ModeOf(copy), Is.EqualTo(mode));
            Assert.That(RateControl.GetMods(copy), Is.EqualTo(mods));
            Assert.That(Score.ReplayData.Get(copy), Is.EqualTo(Score.ReplayData.Get(score)));
            Assert.That(RulesetCompatibility.UnsupportedReplay(copy), Is.False);
        }
        RateControl.Remember(score, new RateSettings(unsupportedRulesetSettings: true));
        using var imported = new MemoryStream();
        using var nativeWriter = (BinaryWriter)Activator.CreateInstance(Score.WriteReplay.Reference.GetParameters()[0].ParameterType, imported)!;
        Score.WriteReplay.Invoke(score, [nativeWriter]);
        imported.Position = 0;
        Assert.That(RulesetCompatibility.UnsupportedReplay(ReadScore(imported)), Is.True);
    }

    [Test]
    public void SelectingNativeMapsClearsCustomFlagsButKeepsManiaMirrorBetweenMaps()
    {
        var map = CreateSelectionTestMap();
        var nativeMode = MethodReader.GetInstructions(StandardModLayout.NativeBeatmapMode)
            .Select(i => i.Operand).OfType<FieldInfo>().Single();
        var oldMode = ChangeStandardRuleset.ModeField.GetValue(null);
        var oldMods = ModManager.ModStatus.Get();
        var observer = typeof(StandardModLayout).GetField("lastSelectionMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousObserver = observer.GetValue(null);
        selectedRulesetTestMap = map;
        harmony.Patch(DifficultyControl.CurrentBeatmap, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(SelectedRulesetMap)));
        try
        {
            ChangeStandardRuleset.ModeField.SetValue(null, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, 0));
            observer.SetValue(null, null);
            StandardModLayout.ObserveSelection();
            ModManager.ModStatus.Set(Mods.DoubleTime | DifficultyControl.Flag | MirrorSettings.Flag);
            nativeMode.SetValue(map, Enum.ToObject(nativeMode.FieldType, 3));
            StandardModLayout.ObserveSelection();
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(Mods.DoubleTime));
            ModManager.ModStatus.Set(Mods.DoubleTime | MirrorSettings.Flag);
            selectedRulesetTestMap = CreateSelectionTestMap();
            nativeMode.SetValue(selectedRulesetTestMap, Enum.ToObject(nativeMode.FieldType, 3));
            StandardModLayout.ObserveSelection();
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(Mods.DoubleTime | MirrorSettings.Flag));
            nativeMode.SetValue(selectedRulesetTestMap, Enum.ToObject(nativeMode.FieldType, 2));
            StandardModLayout.ObserveSelection();
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(Mods.DoubleTime));
            nativeMode.SetValue(selectedRulesetTestMap, Enum.ToObject(nativeMode.FieldType, 3));
            StandardModLayout.ObserveSelection();
            ModManager.ModStatus.Set(Mods.DoubleTime | MirrorSettings.Flag);
            nativeMode.SetValue(selectedRulesetTestMap, Enum.ToObject(nativeMode.FieldType, 0));
            StandardModLayout.ObserveSelection();
            Assert.That(ModManager.ModStatus.Get(), Is.EqualTo(Mods.DoubleTime));
        }
        finally
        {
            harmony.Unpatch(DifficultyControl.CurrentBeatmap, HarmonyPatchType.All, harmony.Id);
            selectedRulesetTestMap = null; observer.SetValue(null, previousObserver);
            ChangeStandardRuleset.ModeField.SetValue(null, oldMode); ModManager.ModStatus.Set(oldMods);
        }
    }

    private static int? rejectedToMode;
    private static string? rejectionMessage;
    private static bool ForceReplayMode(ref bool __result) { __result = true; return false; }
    private static bool CaptureReplayExit(object __0) { rejectedToMode = Convert.ToInt32(__0); return false; }
    private static bool CaptureReplayNotification(string __0) { rejectionMessage = __0; return false; }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void IncompatibleReplayExitsThroughNativeTransitionBeforeGameplayStarts(int mode)
    {
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.PlayMode.ReturnType, mode));
        SetRulesetScoreMods(score, Mods.DoubleTime);
        RateControl.Remember(score, new RateSettings(1.7, unsupportedRulesetSettings: true));
        var oldReplay = Player.ReplayScore.Get();
        var notifications = Osu.StablePlus.Stubs.Graphics.Notifications.NotificationManager.ShowMessage.Reference;
        harmony.Patch(Player.IsReplay.Reference, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(ForceReplayMode)));
        harmony.Patch(RulesetCompatibility.ExitPlayer, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(CaptureReplayExit)));
        harmony.Patch(notifications, prefix: new HarmonyMethod(typeof(StableIntegrationTests), nameof(CaptureReplayNotification)));
        try
        {
            rejectedToMode = null; rejectionMessage = null;
            Player.ReplayScore.Set(score);
            var player = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Player.Class.Reference);
            // No native resources were constructed; its normal finalizer expects
            // an initialized audio/rendering subsystem in the game process.
            GC.SuppressFinalize(player);
            Assert.That(Player.OnLoadComplete.Invoke(player, [true]), Is.True);
            Assert.That(rejectedToMode, Is.EqualTo(Convert.ToInt32(Enum.Parse(GameBase.Mode.Reference.FieldType, "SelectPlay"))));
            Assert.That(rejectionMessage, Does.Contain("lazer mods or settings"));
            Assert.That(rejectionMessage, Does.Contain("Play it in osu!lazer"));
            Assert.That(RateControl.ForScore(score).Speed, Is.EqualTo(1.7));
        }
        finally
        {
            Player.ReplayScore.Set(oldReplay);
            foreach (var method in new[] { Player.IsReplay.Reference, RulesetCompatibility.ExitPlayer, notifications })
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
        }
    }
}
