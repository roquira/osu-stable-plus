using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal static class ModLabelFormatter
{
    internal static string DifficultyDetails(DifficultySettings difficulty, object? map)
    {
        var names = new[] { "HP", "CS", "AR", "OD" };
        var changed = new List<string>();
        for (var i = 0; i < names.Length; i++)
            if (difficulty.Values[i] is { } value &&
                (map == null || Math.Abs(value - DifficultyControl.Default(map, i)) > 0.00001))
                changed.Add(names[i] + value.ToString("0.#", CultureInfo.InvariantCulture));
        return string.Join(",", changed);
    }

    internal static string Format(string text, int mods, RateSettings settings, object? map,
        bool longNames, bool hover, bool showDefaultRate = false)
    {
        var tokens = text.Split(',').Select(t => t.Trim()).Where(t => t.Length != 0).ToList();
        if (!longNames && ModLabelOptions.RelaxAsRx.Value)
            tokens = tokens.Select(t => t == "Relax" ? "RX" : t).ToList();
        var gap = hover ? " " : "";
        if (settings.Mirror is { } axes)
        {
            tokens.RemoveAll(t => t == "MR" || t == "Mirror");
            tokens.Add((longNames ? "Mirror" : "MR") +
                (ModLabelOptions.ShowMirror.Value ? gap + "(" + axes.ToString()[0] + ")" : ""));
        }
        if (CustomRateOptions.IsEnabled(mods) && ModLabelOptions.ShowRate.Value && (showDefaultRate || settings.Speed != RateSettings.DefaultFor(mods)))
        {
            var name = longNames ? SelectedRateDisplay.RateName(mods) : (mods & Nightcore) != 0 ? "NC" : (mods & HalfTime) != 0 ? "HT" : "DT";
            var suffix = gap + "(" + settings.Speed.ToString("0.##", CultureInfo.InvariantCulture) + "x)";
            tokens = tokens.Select(t => t == name ? t + suffix : t).ToList();
        }
        if (settings.Difficulty is { } difficulty)
        {
            var detail = ModLabelOptions.ShowDifficulty.Value ? DifficultyDetails(difficulty, map) : "";
            tokens.Add((longNames ? "DifficultyAdjust" : "DA") + (detail.Length == 0 ? "" : gap + "(" + detail + ")"));
        }
        return string.Join(hover ? ", " : ",", tokens);
    }
}
