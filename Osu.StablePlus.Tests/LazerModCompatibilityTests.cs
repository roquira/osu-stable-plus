using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Tests;

[TestFixture]
public class LazerModCompatibilityTests
{
    [TestCase("DC")]
    [TestCase("WU")]
    [TestCase("WD")]
    [TestCase("BL")]
    [TestCase("RD")]
    [TestCase("AC")]
    public void UnsupportedStandardModsAreNotSilentlyDropped(string acronym)
    {
        var settings = ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"DT\",\"settings\":{\"speed_change\":1.1}}," +
            "{\"acronym\":\"" + acronym + "\"}]}", 64)!;
        Assert.That(settings.UnsupportedRulesetSettings, Is.True);
        Assert.That(settings.Speed, Is.EqualTo(1.1), "Keep independently supported settings for labels.");
    }

    [TestCase("HD", "{\"only_fade_approach_circles\":true}")]
    [TestCase("FL", "{\"follow_delay\":0.3}")]
    [TestCase("DT", "{\"unknown_option\":true}")]
    [TestCase("MR", "{\"reflection\":2,\"unknown_option\":true}")]
    [TestCase("CL", "{\"no_slider_head_accuracy\":false}")]
    [TestCase("CL", "{\"classic_note_lock\":false}")]
    [TestCase("CL", "{\"classic_health\":\"true\"}")]
    [TestCase("DA", "{\"extended_limits\":\"true\"}")]
    public void UnsupportedStandardOptionsAreDetected(string acronym, string options)
    {
        var settings = ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"" + acronym + "\",\"settings\":" + options + "}]}",
            acronym == "DT" ? 64 : 0)!;
        Assert.That(settings.UnsupportedRulesetSettings, Is.True);
    }

    [TestCase("NF")]
    [TestCase("EZ")]
    [TestCase("TD")]
    [TestCase("HD")]
    [TestCase("HR")]
    [TestCase("SD")]
    [TestCase("RX")]
    [TestCase("FL")]
    [TestCase("AT")]
    [TestCase("SO")]
    [TestCase("AP")]
    [TestCase("PF")]
    [TestCase("SV2")]
    [TestCase("CL")]
    public void StandardLegacyModsRemainSupported(string acronym) =>
        Assert.That(ReplayRateMetadata.ReadLazerJson("{\"mods\":[{\"acronym\":\"" + acronym + "\"}]}", 0)?.UnsupportedRulesetSettings ?? false, Is.False);

    [Test]
    public void ExplicitClassicDefaultsAndInRangeDifficultyRemainSupported()
    {
        var settings = ReplayRateMetadata.ReadLazerJson("""
            {"mods":[{"acronym":"CL","settings":{"no_slider_head_accuracy":true,"classic_note_lock":true,
                "always_play_tail_sample":true,"fade_hit_circle_early":true,"classic_health":true}},
                {"acronym":"DA","settings":{"approach_rate":9.5,"extended_limits":true}}]}
            """, 0)!;
        Assert.That(settings.UnsupportedRulesetSettings, Is.False);
        Assert.That(settings.Difficulty!.Values[2], Is.EqualTo(9.5));
    }

    [TestCase("{\"mods\":[null]}")]
    [TestCase("{\"mods\":[{\"acronym\":\"HD\",\"settings\":5}]}")]
    public void MalformedModEntriesAreNotSilentlyIgnored(string json) =>
        Assert.That(ReplayRateMetadata.ReadLazerJson(json, 0)!.UnsupportedRulesetSettings, Is.True);
}
