using System;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Immutable settings, captured for each play independently of the options menu.</summary>
internal sealed class RateSettings
{
    public const double DefaultSpeed = 1.5;
    public double Speed { get; }
    public bool AdjustPitch { get; }
    internal DifficultySettings? Difficulty { get; }
    internal MirrorAxes? Mirror { get; }
    internal StableScoringSettings? Scoring { get; }
    internal bool UnsupportedRulesetSettings { get; }

    public RateSettings(double speed = DefaultSpeed, bool adjustPitch = false, DifficultySettings? difficulty = null, MirrorAxes? mirror = null,
        StableScoringSettings? scoring = null, bool unsupportedRulesetSettings = false)
    {
        if (!IsValidSpeed(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        Speed = speed;
        AdjustPitch = adjustPitch;
        Difficulty = difficulty;
        if (mirror.HasValue && !Enum.IsDefined(typeof(MirrorAxes), mirror.Value)) throw new ArgumentOutOfRangeException(nameof(mirror));
        Mirror = mirror;
        Scoring = scoring;
        UnsupportedRulesetSettings = unsupportedRulesetSettings;
    }

    internal RateSettings WithScoring(StableScoringSettings? scoring) => new(Speed, AdjustPitch, Difficulty, Mirror, scoring, UnsupportedRulesetSettings);

    internal static double DefaultFor(int mods) => (mods & 256) != 0 ? 0.75 : DefaultSpeed;
    public static bool IsValidSpeed(double speed) => IsValidSpeed(speed, false) || IsValidSpeed(speed, true);
    internal static bool IsValidSpeed(double speed, bool halfTime) => !double.IsNaN(speed) &&
        (halfTime ? speed >= 0.5 && speed <= 0.99 : speed >= 1.01 && speed <= 2.0);

    public static double NormalizeSpeed(double speed, bool halfTime = false) => IsValidSpeed(speed, halfTime)
        ? Math.Round(speed, 2, MidpointRounding.AwayFromZero)
        : halfTime ? 0.75 : DefaultSpeed;
}
