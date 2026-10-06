using System;
using System.Globalization;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Null means follow the selected beatmap, as in lazer's DifficultyBindable.</summary>
internal sealed class DifficultySettings
{
    internal readonly double?[] Values;
    internal DifficultySettings(params double?[] values)
    {
        if (values.Length != 4) throw new ArgumentException("Expected HP, CS, AR, OD.");
        Values = (double?[])values.Clone();
        foreach (var value in Values)
            if (value.HasValue && (double.IsNaN(value.Value) || value < 0 || value > 10))
                throw new ArgumentOutOfRangeException(nameof(values));
    }

    internal static double Normalize(double value) => Math.Round(Math.Max(0, Math.Min(10, value)), 1, MidpointRounding.AwayFromZero);
    internal static double Effective(int index, double value, double rate)
    {
        if (index == 3) return (80 - (80 - 6 * value) / rate) / 6;
        if (index != 2) return value;
        var preempt = (value <= 5 ? 1800 - 120 * value : 1200 - 150 * (value - 5)) / rate;
        return preempt >= 1200 ? (1800 - preempt) / 120 : 5 + (1200 - preempt) / 150;
    }

    internal static string Label(int index, double value, double rate)
    {
        var text = new[] { "HP", "CS", "AR", "OD" }[index] + " (" + value.ToString("0.#", CultureInfo.InvariantCulture) + ")";
        return rate != 1 && index >= 2 ? text + " [" + Effective(index, value, rate).ToString("0.#", CultureInfo.InvariantCulture) + "]" : text;
    }
}
