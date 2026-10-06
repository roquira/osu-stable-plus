using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using NUnit.Framework;
using Osu.StablePlus.Hook;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class SettingsDefaultsTests
{
    internal static readonly Dictionary<string, object> Expected = new()
    {
        [nameof(Settings.GameplayModIconOpacity)] = 0,
        [nameof(Settings.DoubleTimeAdjustPitch)] = false,
        [nameof(Settings.HalfTimeAdjustPitch)] = false,
        [nameof(Settings.ShowRelaxAsRx)] = true,
        [nameof(Settings.ShowRateInModLabels)] = true,
        [nameof(Settings.ShowDifficultyInModLabels)] = true,
        [nameof(Settings.ShowMirrorInModLabels)] = true,
        [nameof(Settings.AdjustStableScoring)] = true,
        [nameof(Settings.ApplyRelaxStarReduction)] = false,
        [nameof(Settings.ApplyAutopilotStarReduction)] = false,
        [nameof(Settings.EnableModAudioPreview)] = true,
        [nameof(Settings.ShowPerformanceInGame)] = true,
        [nameof(Settings.ShowPerformanceOnLeaderboard)] = true,
        [nameof(Settings.PerformanceDecimalPlaces)] = 0,
        [nameof(Settings.EstimatePpWithoutRelax)] = true,
        [nameof(Settings.EstimatePpWithoutAutopilot)] = true,
    };

    internal static void AssertDefaults(Settings settings)
    {
        foreach (var entry in Expected)
            Assert.That(typeof(Settings).GetProperty(entry.Key)!.GetValue(settings, null), Is.EqualTo(entry.Value), entry.Key);
    }

    [Test]
    public void FreshAndMissingXmlPreferencesUseRequestedDefaults()
    {
        AssertDefaults(new Settings());
        AssertDefaults(Settings.Default);
        var serializer = new XmlSerializer(typeof(Settings));
        using var input = new StringReader("<Settings />");
        AssertDefaults((Settings)serializer.Deserialize(input));
    }

    [Test]
    public void ExplicitNonDefaultPreferencesSurviveSerialization()
    {
        var settings = new Settings();
        foreach (var entry in Expected)
            typeof(Settings).GetProperty(entry.Key)!.SetValue(settings, entry.Value is bool enabled ? !enabled : (object)2, null);
        var serializer = new XmlSerializer(typeof(Settings));
        using var output = new StringWriter();
        serializer.Serialize(output, settings);
        using var input = new StringReader(output.ToString());
        var reloaded = (Settings)serializer.Deserialize(input);
        foreach (var entry in Expected)
            Assert.That(typeof(Settings).GetProperty(entry.Key)!.GetValue(reloaded, null),
                Is.EqualTo(entry.Value is bool enabled ? !enabled : (object)2), entry.Key);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RemovedCookiePreferenceDoesNotDiscardOtherSettings(bool enabled)
    {
        var serializer = new XmlSerializer(typeof(Settings));
        using var input = new StringReader($"<Settings><UseStablePlusCookie>{enabled.ToString().ToLowerInvariant()}</UseStablePlusCookie>" +
            "<ShowRelaxAsRx>false</ShowRelaxAsRx><PerformanceDecimalPlaces>2</PerformanceDecimalPlaces></Settings>");
        var settings = (Settings)serializer.Deserialize(input);
        Assert.That(settings.ShowRelaxAsRx, Is.False);
        Assert.That(settings.PerformanceDecimalPlaces, Is.EqualTo(2));
        Assert.That(settings.EnableModAudioPreview, Is.True);
        using var output = new StringWriter();
        serializer.Serialize(output, settings);
        Assert.That(output.ToString(), Does.Not.Contain("UseStablePlusCookie"));
    }
}
