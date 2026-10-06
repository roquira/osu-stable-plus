using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class MirrorTests
{
    [TestCase(0, 412, 75)]
    [TestCase(1, 100, 309)]
    [TestCase(2, 412, 309)]
    public void LazerAxesReflectAroundPlayfieldCentre(int axes, double x, double y)
    {
        Assert.That(MirrorSettings.X(100, (MirrorAxes)axes), Is.EqualTo(x));
        Assert.That(MirrorSettings.Y(75, (MirrorAxes)axes), Is.EqualTo(y));
        // Offscreen slider control points must reflect without clamping.
        var reflected = MirrorSettings.X(-120, (MirrorAxes)axes);
        Assert.That(MirrorSettings.X(reflected, (MirrorAxes)axes), Is.EqualTo(-120));
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void StableTrailerRoundTripWithOptionalDifficulty(int axis, bool da)
    {
        using var stream = new MemoryStream();
        ReplayRateMetadata.WriteTail(stream, new RateSettings(1.73, true,
            da ? new DifficultySettings(6, 3, 9.2, 8) : null, (MirrorAxes)axis));
        stream.Position = 0;
        var result = ReplayRateMetadata.ReadTail(stream, 20260909, MirrorSettings.Flag | 64)!;
        Assert.That(result.Mirror, Is.EqualTo((MirrorAxes)axis));
        Assert.That(result.Speed, Is.EqualTo(1.73));
        Assert.That(result.Difficulty != null, Is.EqualTo(da));
        Assert.That(stream.Position, Is.Zero);
    }

    [TestCase("horizontal", 0)]
    [TestCase("vertical", 1)]
    [TestCase("both", 2)]
    public void LazerReflectionImport(string value, int expected)
    {
        var json = "{\"mods\":[{\"acronym\":\"MR\",\"settings\":{\"reflection\":\"" + value + "\"}}]}";
        Assert.That(ReplayRateMetadata.ReadLazerJson(json, 0)!.Mirror, Is.EqualTo((MirrorAxes)expected));
        Assert.That(ReplayRateMetadata.ReadLazerJson(json, MirrorSettings.Flag, 3)!.UnsupportedRulesetSettings, Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void NumericLazerReflectionImport(int axes)
    {
        var json = "{\"mods\":[{\"acronym\":\"MR\",\"settings\":{\"reflection\":" + axes + "}}]}";
        Assert.That(ReplayRateMetadata.ReadLazerJson(json, 0)!.Mirror, Is.EqualTo((MirrorAxes)axes));
    }

    [TestCase("DT", 64, 1.1)]
    [TestCase("NC", 576, 1.3)]
    [TestCase("HT", 256, 0.6)]
    public void NumericMirrorPreservesRateAndDifficulty(string acronym, int flags, double speed)
    {
        var json = new JObject
        {
            ["mods"] = new JArray(
            new JObject { ["acronym"] = acronym, ["settings"] = new JObject { ["speed_change"] = speed } },
            new JObject { ["acronym"] = "MR", ["settings"] = new JObject { ["reflection"] = 2 } },
            new JObject { ["acronym"] = "DA", ["settings"] = new JObject { ["approach_rate"] = 9.5 } },
            new JObject { ["acronym"] = "HD" }, new JObject { ["acronym"] = "RX" })
        };
        var settings = ReplayRateMetadata.ReadLazerJson(json.ToString(), flags | 8 | 128)!;
        Assert.That(settings.Mirror, Is.EqualTo(MirrorAxes.Both));
        Assert.That(settings.Speed, Is.EqualTo(speed));
        Assert.That(settings.AdjustPitch, Is.EqualTo(acronym == "NC"));
        Assert.That(settings.Difficulty!.Values, Is.EqualTo(new double?[] { null, null, 9.5, null }));
        Assert.That(settings.UnsupportedRulesetSettings, Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ExportUsesLazerNumericEnum(int axes)
    {
        var mods = LazerReplayExport.CreateMods(MirrorSettings.Flag, new RateSettings(mirror: (MirrorAxes)axes));
        var reflection = mods.OfType<JObject>().Single(m => (string?)m["acronym"] == "MR")["settings"]!["reflection"]!;
        Assert.That(reflection.Type, Is.EqualTo(JTokenType.Integer));
        Assert.That((int)reflection, Is.EqualTo(axes));
        Assert.That(ReplayRateMetadata.ReadLazerJson(new JObject { ["mods"] = mods }.ToString(), 0)!.Mirror,
            Is.EqualTo((MirrorAxes)axes));
    }

    [TestCase("-1")]
    [TestCase("3")]
    [TestCase("0.5")]
    [TestCase("true")]
    [TestCase("null")]
    [TestCase("\"2\"")]
    [TestCase("{}")]
    public void InvalidMirrorTypesAreRejected(string value) => Assert.Throws<InvalidDataException>(() =>
        ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"MR\",\"settings\":{\"reflection\":" + value + "}}]}", 0));

    [TestCase("Horizontal", 0)]
    [TestCase("Vertical", 1)]
    [TestCase("Both", 2)]
    public void LegacyEnumNamesAreStillAccepted(string value, int axes) =>
        Assert.That(ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"MR\",\"settings\":{\"reflection\":\"" + value + "\"}}]}", 0)!.Mirror,
            Is.EqualTo((MirrorAxes)axes));

    [Test]
    public void LazerDefaultAndInvalidReflection()
    {
        Assert.That(ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"MR\"}]}", 0)!.Mirror, Is.EqualTo(MirrorAxes.Horizontal));
        Assert.Throws<InvalidDataException>(() => ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"MR\",\"settings\":{\"reflection\":\"invalid\"}}]}", 0));
    }
}
