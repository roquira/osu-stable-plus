using JetBrains.Annotations;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Stubs.Root;

namespace Osu.StablePlus.Stubs.Wrappers;

[PublicAPI]
public static class OptionsUtils
{
    /// <summary>
    ///     Reloads the options panel to reset the available options.
    /// </summary>
    /// <param name="scrollToTop">Forcefully scroll to the top after reloading.</param>
    public static void ReloadOptions(bool scrollToTop = true)
    {
        var mainScheduler = GameBase.Scheduler.Get();
        var options = GameBase.Options.Get();

        var task = VoidDelegate.MakeInstance(() =>
            Options.ReloadElements.Invoke(options, [scrollToTop]));

        Scheduler.Add.Invoke(mainScheduler, [
            /* task: */ task,
            /* forceScheduled: */ false,
        ]);
    }
}
