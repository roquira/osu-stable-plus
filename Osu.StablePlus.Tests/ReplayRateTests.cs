using System;
using System.IO;
using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class ReplayRateTests
{
    [TestCase(0.5, false)]
    [TestCase(0.75, false)]
    [TestCase(0.99, true)]
    public void HalfTimeLazerSettingsRoundTrip(double speed, bool pitch)
    {
        var settings = new RateSettings(speed, pitch);
        var json = new Newtonsoft.Json.Linq.JObject { ["mods"] = LazerReplayExport.CreateMods(256, settings) }.ToString();
        var result = ReplayRateMetadata.ReadLazerJson(json, 256)!;
        Assert.That(result.Speed, Is.EqualTo(speed));
        Assert.That(result.AdjustPitch, Is.EqualTo(pitch));
        using var stream = new MemoryStream();
        ReplayRateMetadata.WriteTail(stream, settings);
        stream.Position = 0;
        Assert.That(ReplayRateMetadata.ReadTail(stream, 20250901, 256)!.Speed, Is.EqualTo(speed));
    }

    [Test]
    public void HalfTimeLazerDefaultIsThreeQuartersWithPitchOff()
    {
        var settings = ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"HT\"}]}", 256)!;
        Assert.That(settings.Speed, Is.EqualTo(0.75));
        Assert.That(settings.AdjustPitch, Is.False);
    }

    [TestCase(0.49)]
    [TestCase(1.0)]
    [TestCase(1.5)]
    public void HalfTimeRejectsOutOfRangeLazerValues(double speed)
    {
        var json = new Newtonsoft.Json.Linq.JObject { ["mods"] = LazerReplayExport.CreateMods(256, new RateSettings()) };
        json["mods"]![0]!["settings"]!["speed_change"] = speed;
        Assert.Throws<InvalidDataException>(() => ReplayRateMetadata.ReadLazerJson(json.ToString(), 256));
    }

    [TestCase(1.01, false)]
    [TestCase(1.5, true)]
    [TestCase(1.7, false)]
    [TestCase(2.0, true)]
    public void StableExtensionRoundTrips(double speed, bool pitch)
    {
        using var stream = new MemoryStream();
        ReplayRateMetadata.WriteTail(stream, new RateSettings(speed, pitch));
        stream.Position = 0;
        var result = ReplayRateMetadata.ReadTail(stream, 20250901, 200)!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Speed, Is.EqualTo(speed));
            Assert.That(result.AdjustPitch, Is.EqualTo(pitch));
            Assert.That(stream.Position, Is.Zero);
            Assert.That(stream.CanRead, Is.True);
        });
    }

    [TestCase("{\"acronym\":\"DT\"}", 1.5, false)]
    [TestCase("{\"acronym\":\"DT\",\"settings\":{\"speed_change\":1.7}}", 1.7, false)]
    [TestCase("{\"acronym\":\"DT\",\"settings\":{\"speed_change\":2,\"adjust_pitch\":true}}", 2.0, true)]
    [TestCase("{\"acronym\":\"NC\",\"settings\":{\"speed_change\":1.8}}", 1.8, true)]
    public void LazerDefaultsAndPitch(string mod, double speed, bool pitch)
    {
        var settings = ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"HD\"}," + mod + ",{\"acronym\":\"RX\"}]}", 200)!;
        Assert.That(settings.Speed, Is.EqualTo(speed));
        Assert.That(settings.AdjustPitch, Is.EqualTo(pitch));
    }

    [TestCase("0.75")]
    [TestCase("2.01")]
    [TestCase("\"NaN\"")]
    [TestCase("true")]
    public void InvalidRatesAreRejected(string speed) => Assert.Throws<InvalidDataException>(() =>
        ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"DT\",\"settings\":{\"speed_change\":" + speed + "}}]}", 64));

    [Test]
    public void MetadataIsOptionalForStable()
    {
        using var stream = new MemoryStream();
        Assert.That(ReplayRateMetadata.ReadTail(stream, 20250901, 64), Is.Null);
        Assert.That(ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"HD\"}]}", 8), Is.Null);
    }

    [Test]
    public void TruncatedLazerBlockPreservesReaderPosition()
    {
        using var stream = new MemoryStream(new byte[] { 20, 0, 0, 0, 1 });
        Assert.Throws<InvalidDataException>(() => ReplayRateMetadata.ReadTail(stream, 30000019, 64));
        Assert.That(stream.Position, Is.Zero);
    }

    [Test]
    public void OversizedLzmaDictionaryIsRejectedBeforeAllocating()
    {
        var bytes = new byte[13];
        bytes[0] = 0x5d;
        bytes[4] = 0x7f;
        Assert.Throws<InvalidDataException>(() => ReplayRateMetadata.ReadLazerBlock(bytes, 64));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(0.0)]
    [TestCase(3.0)]
    public void InvalidConfigurationUsesDefault(double value) =>
        Assert.That(RateSettings.NormalizeSpeed(value), Is.EqualTo(1.5));

    [Test]
    public void UserLazerReplay()
    {
        var filename = Environment.GetEnvironmentVariable("OSU_TEST_REPLAY");
        if (string.IsNullOrEmpty(filename)) Assert.Ignore("Set OSU_TEST_REPLAY to the supplied DT 1.7x replay.");
        using var stream = File.OpenRead(filename!);
        var settings = ReplayRateMetadata.ReadReplay(stream)!;
        Assert.That(settings.Speed, Is.EqualTo(1.7));
        Assert.That(settings.AdjustPitch, Is.False);
    }
}
