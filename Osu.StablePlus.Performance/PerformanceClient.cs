using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Osu.StablePlus.Performance;

/// <summary>Owns a single headless worker. All I/O and process startup happen off the game thread.</summary>
public static class PerformanceClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? worker;
    private static StreamWriter? input;
    private static long sequence;
    private static DateTime retryAfter;
    public static string HelperPath { get; set; } = Path.Combine(Path.GetDirectoryName(typeof(PerformanceClient).Assembly.Location)!,
        "performance", Product.HelperExecutable);

    public static Task<CalculationResult> CalculateAsync(CalculationRequest request, CancellationToken token = default) => Task.Run(async () =>
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow < retryAfter) return new CalculationResult { Error = "Performance helper temporarily unavailable." };
            if (worker == null || worker.HasExited)
            {
                Stop();
                worker = Process.Start(new ProcessStartInfo(HelperPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                    WorkingDirectory = Path.GetDirectoryName(HelperPath)!
                }) ?? throw new IOException("Could not start performance helper.");
                // Framework's redirected writer can emit a BOM. The wire protocol is
                // UTF-8 JSON lines, including Unicode beatmap paths, without a preamble.
                input = new StreamWriter(worker.StandardInput.BaseStream, new UTF8Encoding(false));
                worker.ErrorDataReceived += (_, args) => { if (args.Data != null) Console.WriteLine("[Performance helper] " + args.Data); };
                worker.BeginErrorReadLine();
            }
            request.Id = Interlocked.Increment(ref sequence);
            // Do not inherit host-wide JsonConvert.DefaultSettings (naming/type metadata).
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
            using var json = new StringWriter(CultureInfo.InvariantCulture);
            serializer.Serialize(json, request);
            await input!.WriteLineAsync(json.ToString()).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);
            var read = worker.StandardOutput.ReadLineAsync();
            if (await Task.WhenAny(read, Task.Delay(30000)).ConfigureAwait(false) != read)
                throw new TimeoutException("Performance calculation timed out.");
            var line = await read.ConfigureAwait(false) ?? throw new IOException("Performance helper exited.");
            using var response = new StringReader(line);
            var result = serializer.Deserialize<CalculationResult>(new JsonTextReader(response)) ?? throw new IOException("Empty performance response.");
            if (result.Protocol != CalculationRequest.ProtocolVersion || result.Version != CalculationRequest.EngineVersion || result.Id != request.Id)
                throw new IOException("Performance helper response mismatch: " + result.Version + "/" + result.Protocol + "/" + result.Id + " expected " + request.Id + "; " + result.Error);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            Stop(); retryAfter = DateTime.UtcNow.AddSeconds(5);
            Console.WriteLine("[Performance] " + e);
            return new CalculationResult { Error = e.Message };
        }
        finally { Gate.Release(); }
    }, token);

    private static void Stop()
    {
        if (worker == null) return;
        try { input?.Dispose(); if (!worker.HasExited) worker.Kill(); } catch (Exception) { }
        input = null;
        worker.Dispose(); worker = null;
    }
}
