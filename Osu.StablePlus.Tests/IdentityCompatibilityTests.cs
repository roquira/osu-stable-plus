using System;
using System.IO;
using NUnit.Framework;
using Osu.StablePlus.Hook;

namespace Osu.StablePlus.Tests;

[TestFixture, NonParallelizable]
public class IdentityCompatibilityTests
{
    [Test]
    public void LegacyPreferencesAreReadAndSavedWithoutReplacingOriginal()
    {
        var directory = Path.GetFullPath(Path.Combine(".scratch", "identity-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            const string xml = "<Settings><ShowPerformanceInGame>false</ShowPerformanceInGame>" +
                "<ShowPerformanceOnLeaderboard>true</ShowPerformanceOnLeaderboard>" +
                "<EstimatePpWithoutRelax>true</EstimatePpWithoutRelax>" +
                "<EstimatePpWithoutAutopilot>true</EstimatePpWithoutAutopilot>" +
                "<PerformanceDecimalPlaces>2</PerformanceDecimalPlaces>" +
                "<HalfTimeSpeed>0.6</HalfTimeSpeed><Calculator>Akatsuki</Calculator></Settings>";
            var legacy = Path.Combine(directory, Product.LegacySettingsFilename);
            File.WriteAllText(legacy, xml);
            var settings = Settings.ReadFromDisk(directory);
            Assert.That(settings.ShowPerformanceInGame, Is.False);
            Assert.That(settings.ShowPerformanceOnLeaderboard, Is.True);
            Assert.That(settings.EstimatePpWithoutRelax && settings.EstimatePpWithoutAutopilot, Is.True);
            Assert.That(settings.PerformanceDecimalPlaces, Is.EqualTo(2));
            Assert.That(settings.HalfTimeSpeed, Is.EqualTo(0.6));
            settings.PerformanceDecimalPlaces = 1;
            Settings.WriteToDisk(settings, directory);
            Assert.That(File.ReadAllText(legacy), Is.EqualTo(xml));
            Assert.That(File.Exists(Path.Combine(directory, Product.SettingsFilename)), Is.True);
            Assert.That(Settings.ReadFromDisk(directory).PerformanceDecimalPlaces, Is.EqualTo(1),
                "The new preferences file takes precedence over the legacy file.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void UnreadablePreferencesFallBackToDefaultsAndKeepABackup()
    {
        var directory = Path.GetFullPath(Path.Combine(".scratch", "identity-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, Product.SettingsFilename);
            File.WriteAllText(file, "<Settings><HalfTimeSpeed>0.6");
            Assert.That(Settings.ReadFromDisk(directory), Is.SameAs(Settings.Default));
            Assert.That(File.ReadAllText(file + ".invalid"), Is.EqualTo("<Settings><HalfTimeSpeed>0.6"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void SavingReplacesPreferencesWithoutLeavingTemporaryFiles()
    {
        var directory = Path.GetFullPath(Path.Combine(".scratch", "identity-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            Settings.WriteToDisk(new Settings { PerformanceDecimalPlaces = 1 }, directory);
            Settings.WriteToDisk(new Settings { PerformanceDecimalPlaces = 2 }, directory);
            Assert.That(Settings.ReadFromDisk(directory).PerformanceDecimalPlaces, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(directory), Is.EquivalentTo(new[] { Path.Combine(directory, Product.SettingsFilename) }));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestCase("osu!.hook", true)]
    [TestCase("OSU!.HOOK", true)]
    [TestCase("osu-stable-plus-hook", true)]
    [TestCase("osu-stable-plus-performance", false)]
    [TestCase("osu!", false)]
    public void RecognizesBothHookAssemblyIdentities(string name, bool expected) =>
        Assert.That(InjectionGuard.IsHookAssembly(name), Is.EqualTo(expected));

    [Test]
    public void ProcessWideGuardRejectsDuplicateAndAllowsPrePatchRetry()
    {
        Assert.That(InjectionGuard.TryAcquire(), Is.True);
        try { Assert.That(InjectionGuard.TryAcquire(), Is.False); }
        finally { InjectionGuard.Release(); }
        Assert.That(InjectionGuard.TryAcquire(), Is.True);
        InjectionGuard.Release();
    }

    [Test]
    public void InjectionEntryPointAndResourcePrefixResolveAfterRename()
    {
        var assembly = typeof(Hook.Hook).Assembly;
        Assert.That(assembly.GetName().Name, Is.EqualTo(Product.HookAssembly));
        Assert.That(assembly.GetType(Product.HookType)?.GetMethod("Initialize"), Is.Not.Null);
        Assert.That(assembly.GetManifestResourceNames(), Has.Some.StartsWith("Osu.StablePlus.Hook.Resources."));
    }
}
