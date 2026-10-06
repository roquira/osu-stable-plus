using System;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>A play's scoring contract, independent of subsequent global preferences.</summary>
internal sealed class StableScoringSettings
{
    internal const int CurrentVersion = 2;
    internal int Version { get; }
    internal int? DifficultyFactor { get; }
    internal bool Adjusted => Version is 1 or 2;

    internal StableScoringSettings(int version, int? difficultyFactor = null)
    {
        if (version < 0 || version > CurrentVersion || difficultyFactor < 0 || difficultyFactor > 8)
            throw new ArgumentOutOfRangeException(nameof(version), "Unsupported stable scoring settings.");
        Version = version;
        DifficultyFactor = difficultyFactor;
    }
}

internal static class StableScoreMath
{
    internal const int ScoreV2 = 1 << 29;
    internal const int Assistance = 128 | 8192;

    internal static double RateMultiplier(int mods, double rate, int mode = 0)
    {
        var halfTime = mode == 3 ? .5 : .3;
        if ((mods & 256) != 0)
        {
            // Preserve version 1's floating-point evaluation order for existing
            // standard replays, including truncation at the scoring boundary.
            if (mode == 0) return rate == .75 ? .30 : rate <= .75 ? .4 * rate : .30 + 2.8 * (rate - .75);
            return rate == 0.75 ? halfTime : rate <= 0.75 ? halfTime * rate / .75 : halfTime + (1 - halfTime) * (rate - .75) / .25;
        }
        if ((mods & (64 | 512)) != 0)
            return mode == 3 ? 1 : rate == 1.5 ? (mode == 2 ? 1.06 : 1.12) : 1 + (mode == 2 ? .12 : .24) * (rate - 1);
        return 1;
    }

    internal static double RelativeRate(int mods, double rate, int mode = 0)
    {
        if ((mods & (64 | 256 | 512)) == 0 || rate == RateSettings.DefaultFor(mods)) return 1;
        return RateMultiplier(mods, rate, mode) / RateMultiplier(mods, RateSettings.DefaultFor(mods), mode);
    }

    internal static int AssistedAward(int award, int mods, double rate)
    {
        var factor = RelativeRate(mods, rate);
        return factor == 1 ? award : checked((int)(award * factor));
    }
}
