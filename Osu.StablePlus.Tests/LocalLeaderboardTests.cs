using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Osu.StablePlus.Hook.Patches.LivePerformance;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Tests;

public partial class StableIntegrationTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void LocalLeaderboardSortsUnorderedScoresBeforeAssigningRanks(int mode)
    {
        var scores = new[] { LocalScore(194997, mode), LocalScore(206460, mode), LocalScore(205460, mode) };
        WithLocalScores(scores, read =>
        {
            var result = (IList)read(mode)!;
            Assert.That(result.Cast<object>(), Is.EqualTo(new[] { scores[1], scores[2], scores[0] }));
            var totals = result.Cast<object>().Select(score => (int)NativeStableScoring.ScoreGetter.Invoke(score, null)).ToArray();
            Assert.That(totals, Is.EqualTo(new[] { 206460, 205460, 194997 }));
            Assert.That(totals[0] - totals[1], Is.EqualTo(1000));
            Assert.That(totals[1] - totals[2], Is.EqualTo(10463));
            Assert.That(read((mode + 1) % 4), Is.Empty, "Retain native mode filtering.");
        });
    }

    [Test]
    public void LocalLeaderboardPreservesNativeTimestampTiesAndIdenticalScoreOrder()
    {
        var timestamp = MethodReader.GetInstructions(SortLocalScores.Comparison).Select(instruction => instruction.Operand)
            .OfType<FieldInfo>().First(field => field.FieldType == typeof(DateTime));
        var later = LocalScore(206460, 0);
        var earlier = LocalScore(206460, 0);
        var identical = LocalScore(206460, 0);
        timestamp.SetValue(later, new DateTime(2026, 10, 5));
        timestamp.SetValue(earlier, new DateTime(2026, 10, 4));
        timestamp.SetValue(identical, new DateTime(2026, 10, 4));
        WithLocalScores([later, earlier, identical], read =>
            Assert.That(((IList)read(0)!).Cast<object>(), Is.EqualTo(new[] { earlier, identical, later })));
    }

    [Test]
    public void LocalLeaderboardHandlesMissingEmptyAndSingleScoreLists()
    {
        WithLocalScores([], read => Assert.That(read(0), Is.Empty));
        var score = LocalScore(194997, 0);
        WithLocalScores([score], read => Assert.That(((IList)read(0)!).Cast<object>(), Is.EqualTo(new[] { score })));
    }

    private static object LocalScore(int total, int mode)
    {
        var score = Activator.CreateInstance(Score.Class.Reference, true)!;
        NativeStableScoring.ScoreSetter.Invoke(score, [total]);
        StandardModLayout.ScoreMode.SetValue(score, Enum.ToObject(StandardModLayout.ScoreMode.FieldType, mode));
        return score;
    }

    private static void WithLocalScores(object[] scores, Action<Func<int, object?>> check)
    {
        var reader = SortLocalScores.Target();
        var database = MethodReader.GetInstructions(reader).Select(instruction => instruction.Operand).OfType<FieldInfo>()
            .Distinct().Single(field => field.IsStatic && field.FieldType.IsGenericType &&
                field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                field.FieldType.GetGenericArguments()[0] == typeof(string));
        var previous = database.GetValue(null);
        var temporary = (IDictionary)Activator.CreateInstance(database.FieldType)!;
        var source = (IList)Activator.CreateInstance(database.FieldType.GetGenericArguments()[1])!;
        foreach (var score in scores) source.Add(score);
        const string mapHash = "local-leaderboard-regression";
        temporary.Add(mapHash, source);
        database.SetValue(null, temporary);
        try
        {
            Assert.That(reader.Invoke(null, ["missing-map", Enum.ToObject(reader.GetParameters()[1].ParameterType, 0)]), Is.Null);
            check(mode => reader.Invoke(null, [mapHash, Enum.ToObject(reader.GetParameters()[1].ParameterType, mode)]));
            Assert.That(source.Cast<object>(), Is.EqualTo(scores), "Sorting the filtered view must not reorder the saved database.");
        }
        finally { database.SetValue(null, previous); }
    }
}
