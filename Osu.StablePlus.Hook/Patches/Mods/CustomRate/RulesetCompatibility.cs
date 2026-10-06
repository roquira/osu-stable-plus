using System;
using System.Linq;
using System.Reflection;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.Graphics.Notifications;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal static class RulesetCompatibility
{
    internal static bool UnsupportedReplay(object score)
    {
        var settings = RateControl.ForScore(score);
        if (settings.UnsupportedRulesetSettings) return true;
        if (StandardModLayout.ModeOf(score) == 0) return false;
        var mods = RateControl.GetMods(score);
        return settings.Difficulty != null || settings.Mirror != null ||
            (mods & DifficultyControl.Flag) != 0 ||
            (!ModeRateTiming.Supports(StandardModLayout.ModeOf(score)) && CustomRateOptions.IsEnabled(mods) && (settings.Speed != RateSettings.DefaultFor(mods) ||
                settings.AdjustPitch != ((mods & Osu.StablePlus.Stubs.Root.Mods.Nightcore) != 0)));
    }

    // Use the same exit transition as stable's load-failure branch, but show a
    // precise explanation instead of attempting to play incompatible frames.
    internal static readonly MethodInfo ExitPlayer = MethodReader.GetInstructions(Player.OnLoadComplete.Reference)
        .Select(i => i.Operand).OfType<MethodInfo>().First(m => m.IsStatic && m.ReturnType == typeof(void) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { GameBase.Mode.Reference.FieldType, typeof(bool) }));

    internal static bool AllowLoadedReplay()
    {
        if (!Player.IsReplay.Invoke() || Player.ReplayScore.Get() is not { } replay || !UnsupportedReplay(replay)) return true;
        ExitPlayer.Invoke(null, [Enum.Parse(GameBase.Mode.Reference.FieldType, "SelectPlay"), true]);
        NotificationManager.ShowMessage.Invoke(null, [
            !ModeRateTiming.Supports(StandardModLayout.ModeOf(replay))
                ? $"Custom-rate playback is unavailable in this mode because required timing hooks failed. See {Product.LogFilename}; native-rate gameplay remains available."
                : "This replay uses lazer mods or settings that osu!stable+ cannot reproduce. Play it in osu!lazer.",
            Color.Orange, 8000, null]);
        return false;
    }
}
