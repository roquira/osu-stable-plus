using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Hook.Patches;
using Osu.StablePlus.Hook.Patches.CustomStrings;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Tests;

[TestFixture, NonParallelizable]
public class EnumNameIntegrationTests
{
    private enum SignedEnum { All = -1, None = 0 }
    private enum WideEnum : ulong { All = ulong.MaxValue }
    private Harmony harmony = null!;
    private MethodInfo triggerFactory = null!;
    private readonly Dictionary<string, string> nativeTriggers = new();
    private readonly Dictionary<string, string> nativeNames = new();
    private static readonly string[] TriggerNames =
    [
        "HitSound", "HitSoundWhistle", "HitSoundFinish", "HitSoundClap", "HitSoundNormalWhistle",
        "HitSoundSoftClap", "HitSoundDrumFinish", "Passing", "Failing"
    ];
    private static readonly object[] Values =
    [
        -1, 0, 1, long.MaxValue, ulong.MaxValue, SignedEnum.All, WideEnum.All, "invalid", true
    ];

    [OneTimeSetUp]
    public void Setup()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OSU_PATH")))
            Assert.Ignore("Set OSU_PATH to an installed stable directory for enum/storyboard integration tests.");
        var assembly = Osu.StablePlus.Stubs.Tests.OsuLoader.UpdateAndLoad().GetAwaiter().GetResult();
        var sampleSet = assembly.GetType("osu.Audio.SampleSet", true)!;
        // Locate the native trigger factory through its hitsound constructor, not an obfuscated name.
        triggerFactory = assembly.GetTypes().Where(t => t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetMethods(AccessTools.allDeclared).Where(m => m.IsStatic && m.ReturnType == t &&
                m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string) })))
            .Single(m => MethodReader.GetInstructions(m).Select(i => i.Operand).OfType<ConstructorInfo>()
                .Any(c => c.DeclaringType!.GetFields(AccessTools.all).Any(f => f.FieldType == sampleSet)));
        foreach (var name in TriggerNames) nativeTriggers[name] = ParseTrigger(name);
        foreach (var type in EnumTypes())
            foreach (var value in Values)
                nativeNames[Key(type, value)] = GetNameOrError(type, value);
        harmony = new Harmony("stableplus.enum-name-tests");
        new OsuPatchProcessor(harmony, typeof(AddEnumValueNames)).Patch();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (harmony == null) return;
        foreach (var method in harmony.GetPatchedMethods().ToArray())
            harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
    }

    private static IEnumerable<Type> EnumTypes() =>
        new[] { typeof(DayOfWeek), typeof(SignedEnum), typeof(WideEnum), OsuString.Class.Reference };

    private static string Key(Type type, object value) => type.FullName + ":" + value.GetType().FullName + ":" + value;

    private static string GetNameOrError(Type type, object value)
    {
        try { return "name:" + Enum.GetName(type, value); }
        catch (ArgumentException error) { return "error:" + error.GetType().FullName + ":" + error.ParamName; }
    }

    private string ParseTrigger(string name) => triggerFactory.Invoke(null, new object[] { name })!.ToString()!;

    [Test]
    public void UnrelatedAndOutOfRangeEnumsKeepNativeBehaviour()
    {
        foreach (var type in EnumTypes())
            foreach (var value in Values)
                Assert.That(GetNameOrError(type, value), Is.EqualTo(nativeNames[Key(type, value)]), Key(type, value));
        Assert.That(SignedEnum.All.ToString(), Is.EqualTo("All"));
        Assert.That(WideEnum.All.ToString(), Is.EqualTo("All"));
        Assert.Throws<ArgumentNullException>(() => Enum.GetName(typeof(SignedEnum), null!));
        Assert.Throws<ArgumentNullException>(() => Enum.GetName(null!, 0));
        Assert.Throws<ArgumentException>(() => Enum.GetName(typeof(string), 0));
    }

    [Test]
    public void CustomLocalisationEnumNamesStillResolve()
    {
        const string name = "StablePlus_EnumRegressionTest";
        var index = CustomStrings.AddOsuString(name, "regression test");
        try
        {
            Assert.That(Enum.GetName(OsuString.Class.Reference, index), Is.EqualTo(name));
            Assert.That(Enum.ToObject(OsuString.Class.Reference, index).ToString(), Is.EqualTo(name));
            Assert.That(Enum.GetName(OsuString.Class.Reference, (long)index), Is.EqualTo(name));
        }
        finally
        {
            CustomStrings.OsuStringNames.Remove((uint)index);
            CustomStrings.OsuStrings.Remove((uint)index);
        }
    }

    [TestCaseSource(nameof(TriggerNames))]
    public void HitsoundAndStateTriggersMatchUnpatchedStable(string name) =>
        Assert.That(ParseTrigger(name), Is.EqualTo(nativeTriggers[name]));

    [Test]
    public void SuppliedStoryboardTriggerBlocksParseWithoutErrors()
    {
        // Optional local fixtures are never bundled or hardcoded to a user's Songs directory.
        var paths = Environment.GetEnvironmentVariable("OSU_TEST_STORYBOARDS");
        if (string.IsNullOrEmpty(paths)) Assert.Ignore("Set OSU_TEST_STORYBOARDS to semicolon-separated .osb paths.");
        var count = 0;
        foreach (var path in paths!.Split(';'))
            foreach (var line in File.ReadLines(path).Select(s => s.TrimStart()))
            {
                if (!line.StartsWith("T,", StringComparison.Ordinal)) continue;
                var name = line.Split(',')[1];
                Assert.That(ParseTrigger(name), Is.Not.Null.And.Not.Empty, path + ": " + line);
                count++;
            }
        Assert.That(count, Is.GreaterThan(0));
        TestContext.WriteLine("Parsed " + count + " native storyboard trigger blocks.");
    }

    [Test]
    public void HookDoesNotInstallAnOsuDirectPermissionBypass() =>
        Assert.That(typeof(AddEnumValueNames).Assembly.GetTypes().Where(OsuPatchProcessor.IsOsuPatch)
            .Any(t => t.Name == "EnableOsuDirect" || t.Namespace?.Contains("BeatmapMirror") == true), Is.False);
}
