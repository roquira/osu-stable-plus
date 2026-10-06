namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

// Match lazer's numeric OsuModMirror.MirrorType values in replay metadata.
internal enum MirrorAxes { Horizontal, Vertical, Both }

internal static class MirrorSettings
{
    internal const int Flag = 1 << 30;
    internal static MirrorAxes Selected = MirrorAxes.Horizontal;
    internal static bool Enabled => StandardModLayout.IsStandard &&
        (Osu.StablePlus.Stubs.GameplayElements.Scoring.ModManager.ModStatus.Get() & Flag) != 0;
    internal static double X(double x, MirrorAxes axes) => axes != MirrorAxes.Vertical ? 512 - x : x;
    internal static double Y(double y, MirrorAxes axes) => axes != MirrorAxes.Horizontal ? 384 - y : y;
}
