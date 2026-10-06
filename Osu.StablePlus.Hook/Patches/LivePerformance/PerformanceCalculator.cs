using System;
using System.Threading;
using System.Threading.Tasks;
using Osu.StablePlus.Performance;
using Osu.StablePlus.Stubs.GameModes.Play;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

internal static class PerformanceCalculator
{
    private static CancellationTokenSource? session;
    private static object? score;
    private static CalculationRequest? pending;
    private static int lastHits;
    private static double? lastTime;
    private static int lastCapture;
    private static bool running;

    public static void ResetCalculator()
    {
        session?.Cancel(); session = null; pending = null; score = null; lastHits = 0;
        lastTime = null;
        PerformanceDisplay.SetText("");
        if (!PerformanceOptions.ShowPerformanceInGame.Value) return;
        var current = Player.CurrentScore.Get();
        if (current == null) return;
        score = current; session = new CancellationTokenSource();
        PerformanceDisplay.Reset();
    }

    internal static void Observe(object current, double? progress = null)
    {
        try
        {
            if (!PerformanceOptions.ShowPerformanceInGame.Value) { ResetCalculator(); return; }
            if (!ReferenceEquals(score, current) || session == null) ResetCalculator();
            if (session == null) return;
            if (progress.HasValue && lastTime.HasValue && progress < lastTime - 1) ResetCalculator();
            lastTime = progress;
            if (progress.HasValue && unchecked(Environment.TickCount - lastCapture) < 100) return;
            lastCapture = Environment.TickCount;
            var request = PerformanceRequests.Capture(current);
            request.Partial = true; request.LegacyTotal = null;
            request.ProgressTime = progress;
            request.StableEstimate = Player.IsReplay.Invoke();
            int hits = request.N300 + request.N100 + request.N50 + request.Misses + request.Geki + request.Katu;
            if (hits < lastHits) ResetCalculator(); // backwards seek/checkpoint
            lastHits = hits;
            pending = request;
            if (!running) { running = true; _ = Process(); }
        }
        catch (Exception e) { Console.WriteLine("[Performance capture] " + e); PerformanceDisplay.SetText("pp unavailable"); }
    }

    // Every mutable session field is accessed on stable's update thread.
    private static async Task Process()
    {
        if (pending == null || session == null) { running = false; return; }
        var request = pending; pending = null;
        var generation = session; var token = generation.Token; var capturedScore = score;
        CalculationResult result;
        try { result = await PerformanceClient.CalculateAsync(request, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { result = new CalculationResult { Error = "Cancelled" }; }
        catch (Exception e) { result = new CalculationResult { Error = e.Message }; }
        await Task.Delay(100).ConfigureAwait(false);
        PerformanceDisplay.Schedule(() =>
        {
            if (ReferenceEquals(generation, session) && ReferenceEquals(capturedScore, Player.CurrentScore.Get()) &&
                !token.IsCancellationRequested && PerformanceOptions.ShowPerformanceInGame.Value && request.WithoutRelax == (request.Mode != 3 && PerformanceOptions.WithoutRelax.Value) &&
                request.WithoutAutopilot == (request.Mode == 0 && PerformanceOptions.WithoutAutopilot.Value))
                PerformanceDisplay.SetResult(result);
            running = false;
            if (pending != null && session != null) { running = true; _ = Process(); }
        });
    }
}
