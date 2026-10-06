using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;

namespace Osu.StablePlus.Performance.Engine.Tests;

[TestFixture]
public class LazerReplayContractTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void OfficialMirrorSerializerUsesNumericAxes(int axes)
    {
        var mirror = new OsuModMirror { Reflection = { Value = (OsuModMirror.MirrorType)axes } };
        var json = JObject.Parse(JsonConvert.SerializeObject(new APIMod(mirror)));
        // Default Horizontal is omitted; non-default axes are numeric, not names.
        if (axes != 0)
        {
            Assert.That(json["settings"]!["reflection"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That((int)json["settings"]!["reflection"]!, Is.EqualTo(axes));
        }
        var imported = JsonConvert.DeserializeObject<APIMod>("{\"acronym\":\"MR\",\"settings\":{\"reflection\":" + axes + "}}")!;
        Assert.That(((OsuModMirror)imported.ToMod(new OsuRuleset())).Reflection.Value, Is.EqualTo(mirror.Reflection.Value));
    }

    [Test]
    public void ReportedChocolateScrambleScoreIsClassicDisplayNotImportReduction()
    {
        var score = new ScoreInfo
        {
            Ruleset = new OsuRuleset().RulesetInfo,
            TotalScore = 1041199,
            MaximumStatistics = new Dictionary<HitResult, int> { [HitResult.Great] = 1339 }
        };
        Assert.That(score.GetDisplayScore(ScoringMode.Standardised), Is.EqualTo(1041199));
        Assert.That(score.GetDisplayScore(ScoringMode.Classic), Is.EqualTo(60905390));
        Assert.That(score.TotalScore, Is.EqualTo(1041199), "Classic display does not rewrite the original total.");
    }
}
