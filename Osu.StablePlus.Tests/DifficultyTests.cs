using System;
using System.IO;
using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class DifficultyTests
{
    [TestCase(3, 9, 1.25, "OD (9) [9.9]")]
    [TestCase(3, 9, 1.5, "OD (9) [10.4]")]
    [TestCase(2, 9.5, 1.25, "AR (9.5) [10.2]")]
    [TestCase(2, 9.5, 1.5, "AR (9.5) [10.7]")]
    [TestCase(2, 0, 2, "AR (0) [7]")]
    [TestCase(0, 5.5, 2, "HP (5.5)")]
    [TestCase(1, 4, 2, "CS (4)")]
    public void EffectiveLabels(int index, double value, double rate, string expected) =>
        Assert.That(DifficultySettings.Label(index, value, rate), Is.EqualTo(expected));

    [TestCase(0)]
    [TestCase(64)]
    [TestCase(576)]
    public void StableDifficultyTrailerRoundTripsWithOrWithoutDt(int mods)
    {
        using var stream = new MemoryStream();
        var settings = new RateSettings(1.7, true, new DifficultySettings(4, null, 9.5, 9));
        ReplayRateMetadata.WriteTail(stream, settings);
        stream.Position = 0;
        var read = ReplayRateMetadata.ReadTail(stream, 20260711, mods)!;
        Assert.That(read.Difficulty!.Values, Is.EqualTo(settings.Difficulty!.Values));
        Assert.That(read.Speed, Is.EqualTo(1.7));
        Assert.That(stream.Position, Is.Zero);
    }

    [Test]
    public void LazerDifficultyWithoutDt()
    {
        var read = ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"DA\",\"settings\":{\"circle_size\":4,\"approach_rate\":9.5}}]}", 0)!;
        Assert.That(read.Difficulty!.Values, Is.EqualTo(new double?[] { null, 4, 9.5, null }));
    }

    [Test]
    public void SuppliedLazerDifficultyReplay()
    {
        var path = Environment.GetEnvironmentVariable("OSU_TEST_DA_REPLAY");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set OSU_TEST_DA_REPLAY.");
        using var stream = File.OpenRead(path!);
        var settings = ReplayRateMetadata.ReadReplay(stream)!;
        TestContext.WriteLine("DA: " + string.Join(", ", settings.Difficulty!.Values));
        Assert.That(settings.Speed, Is.EqualTo(2));
        Assert.That(settings.Difficulty, Is.Not.Null);
    }
}
