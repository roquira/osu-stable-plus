using System;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Stubs.Graphics;
using Osu.StablePlus.Performance;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

internal static class PerformanceDisplay
{
    private static readonly WeakReference<object?> Counter = new(null);
    private static readonly WeakReference<object?> Status = new(null);
    private static string lastText = "";
    private static CalculationResult? lastResult = new();
    internal static void Reset() => SetResult(new CalculationResult());
    public static void SetPerformanceCounter(object sprite, object status)
    {
        Counter.SetTarget(sprite); Status.SetTarget(status);
        if (lastResult != null) SetResult(lastResult); else SetText(lastText);
    }
    internal static void SetText(string text)
    {
        lastResult = null;
        lastText = text;
        if (Counter.TryGetTarget(out var sprite) && sprite != null)
        {
            pText.SetText.Invoke(sprite, [""]);
            if (Status.TryGetTarget(out var status) && status != null)
            {
                pDrawable.Position.Set(status, pDrawable.Position.Get(sprite));
                pText.SetText.Invoke(status, [text]);
            }
        }
    }
    internal static void SetResult(CalculationResult result)
    {
        if (result.Error != null) { SetText("pp unavailable"); return; }
        lastResult = result;
        if (!Counter.TryGetTarget(out var sprite) || sprite == null) return;
        pText.SetText.Invoke(sprite, [PerformanceRequests.Number(result.Pp) + "pp"]);
        // Status messages (errors, progress) are cleared once a value is available.
        if (Status.TryGetTarget(out var status) && status != null) pText.SetText.Invoke(status, [""]);
    }
    internal static void RefreshFormatting()
    {
        if (lastResult != null) SetResult(lastResult);
        LocalScorePerformance.RefreshFormatting();
    }
    internal static void Schedule(Action action) => Scheduler.Add.Invoke(GameBase.Scheduler.Get(), [VoidDelegate.MakeInstance(action), true]);
}
