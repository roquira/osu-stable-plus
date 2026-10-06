using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Osu.StablePlus.Performance;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

internal static class LocalScorePerformance
{
    private const string Pending = "pp …";
    private static readonly ConditionalWeakTable<object, Entry> Entries = new();
    private static readonly Dictionary<string, CalculationResult> Cache = new();
    private static readonly List<WeakReference<Entry>> VisibleEntries = new();
    internal static readonly FieldInfo TextField = MethodReader.GetInstructions(pText.SetText.Reference)
        .Where(i => i.Opcode == Stfld).Select(i => (FieldInfo)i.Operand).Last();

    internal static string ScoreLine(string text, object score) => Decorate(text, score, false);

    internal static string Decorate(string text, object score, bool details)
    {
        if (!PerformanceOptions.ShowPerformanceOnLeaderboard.Value) return text;
        try
        {
            Get(score);
            // Binding replaces this placeholder immediately when a cached result exists.
            // Keeping the original template also lets precision changes reformat visible rows.
            return text + (details ? "\n" : " · ") + Pending;
        }
        catch (Exception e) { Console.WriteLine("[Local pp] " + e.Message); return text + " · pp unavailable"; }
    }

    private static Entry Get(object score)
    {
        var preferences = PerformanceOptions.WithoutRelax.Value + "/" + PerformanceOptions.WithoutAutopilot.Value;
        if (Entries.TryGetValue(score, out var old) && old.Preferences == preferences &&
            (old.Result?.Error == null || DateTime.UtcNow < old.RetryAfter)) return old;
        var request = PerformanceRequests.Capture(score);
        var file = (string)Score.GetReplayFilename.Reference.Invoke(score, null);
        var entry = new Entry(preferences);
        VisibleEntries.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (VisibleEntries.Count >= 256) VisibleEntries.RemoveAt(0);
        VisibleEntries.Add(new WeakReference<Entry>(entry));
        Entries.Remove(score); Entries.Add(score, entry);
        _ = Load(entry, request, file, OriginalScoreInfo.Snapshot(score));
        return entry;
    }

    private static async Task Load(Entry entry, CalculationRequest request, string replay, string? original)
    {
        CalculationResult result;
        try
        {
            result = await Task.Run(async () =>
            {
                var stable = JsonConvert.SerializeObject(request);
                if (original != null) PerformanceRequests.ApplyOrigin(request, JObject.Parse(original));
                else PerformanceRequests.ReadOrigin(request, replay);
                var key = CalculationRequest.EngineVersion + "/" + ReplayHeader.Hash(File.ReadAllBytes(request.MapPath)) + "/" + JsonConvert.SerializeObject(request);
                lock (Cache) if (Cache.TryGetValue(key, out var cached)) return cached;
                var saved = ReadResult(key);
                if (saved != null) return saved;
                var computed = await PerformanceClient.CalculateAsync(request).ConfigureAwait(false);
                if (request.NativeLazer && computed.Error?.StartsWith("Incomplete", StringComparison.Ordinal) == true)
                {
                    request = JsonConvert.DeserializeObject<CalculationRequest>(stable)!;
                    request.StableEstimate = true; request.LegacyTotal = null; request.VanillaScoring = false;
                    computed = await PerformanceClient.CalculateAsync(request).ConfigureAwait(false);
                }
                if (computed.Error == null)
                {
                    lock (Cache) { if (Cache.Count >= 256) Cache.Clear(); Cache[key] = computed; }
                    SaveResult(key, computed);
                }
                return computed;
            }).ConfigureAwait(false);
        }
        catch (Exception e) { result = new CalculationResult { Error = e.Message }; }
        PerformanceDisplay.Schedule(() =>
        {
            entry.Result = result;
            entry.RetryAfter = DateTime.UtcNow.AddSeconds(5);
            if (!PerformanceOptions.ShowPerformanceOnLeaderboard.Value || entry.Preferences !=
                PerformanceOptions.WithoutRelax.Value + "/" + PerformanceOptions.WithoutAutopilot.Value)
            { entry.Bindings.Clear(); return; }
            Refresh(entry);
        });
    }

    internal static void RefreshFormatting()
    {
        if (!PerformanceOptions.ShowPerformanceOnLeaderboard.Value) return;
        var preferences = PerformanceOptions.WithoutRelax.Value + "/" + PerformanceOptions.WithoutAutopilot.Value;
        foreach (var reference in VisibleEntries)
            if (reference.TryGetTarget(out var entry) && entry.Preferences == preferences) Refresh(entry);
    }

    private static void Refresh(Entry entry)
    {
        entry.Bindings.RemoveAll(binding => !binding.Sprite.TryGetTarget(out _));
        if (entry.Result == null) return;
        foreach (var binding in entry.Bindings) Apply(binding, entry.Result);
    }

    private static void Apply(Binding binding, CalculationResult result)
    {
        if (!binding.Sprite.TryGetTarget(out var sprite)) return;
        var text = binding.Template.Replace(Pending, binding.Tooltip ? PerformanceRequests.Details(result) : PerformanceRequests.Short(result));
        if (binding.Tooltip) NativeModMenu.ModTooltip.SetValue(sprite, text);
        else pText.SetText.Invoke(sprite, [text]);
    }

    private static void Bind(Entry entry, Binding binding)
    {
        binding.Sprite.TryGetTarget(out var sprite);
        entry.Bindings.RemoveAll(previous => !previous.Sprite.TryGetTarget(out var target) ||
            (ReferenceEquals(sprite, target) && previous.Tooltip == binding.Tooltip));
        entry.Bindings.Add(binding);
        if (entry.Result != null) Apply(binding, entry.Result);
    }

    private static string ResultPath(string key) => Path.Combine(Path.GetDirectoryName(OriginalScoreInfo.DirectoryPath)!,
        "results-v" + CalculationRequest.ProtocolVersion + "-" + CalculationRequest.EngineVersion, ReplayHeader.Hash(System.Text.Encoding.UTF8.GetBytes(key)) + ".json");

    private static CalculationResult? ReadResult(string key)
    {
        try
        {
            var file = ResultPath(key);
            if (!File.Exists(file) || new FileInfo(file).Length > 16384) return null;
            var value = JsonConvert.DeserializeObject<CalculationResult>(File.ReadAllText(file));
            return value?.Error == null && value?.Version == CalculationRequest.EngineVersion &&
                value.Protocol == CalculationRequest.ProtocolVersion && !double.IsNaN(value.Pp) && !double.IsInfinity(value.Pp) ? value : null;
        }
        catch (Exception e) { Console.WriteLine("[PP cache read] " + e.Message); return null; }
    }

    private static void SaveResult(string key, CalculationResult result)
    {
        try
        {
            lock (Cache)
            {
                var file = ResultPath(key);
                var directory = Path.GetDirectoryName(file)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(file, JsonConvert.SerializeObject(result));
                foreach (var old in new DirectoryInfo(directory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(256)) old.Delete();
            }
        }
        catch (Exception e) { Console.WriteLine("[PP cache write] " + e.Message); }
    }

    // Called immediately after native row text construction; preserve all its native positioning and animation.
    internal static void AttachText(object sprite, object? score)
    {
        if (score == null || !PerformanceOptions.ShowPerformanceOnLeaderboard.Value) return;
        try
        {
            var text = (string)TextField.GetValue(sprite);
            if (text == null || !text.Contains(Pending)) return;
            Bind(Get(score), new Binding(sprite, text, false));
        }
        catch (Exception e) { Console.WriteLine("[Local pp binding] " + e.Message); }
    }

    internal static void AssignTooltip(object sprite, string text, object? score)
    {
        NativeModMenu.ModTooltip.SetValue(sprite, text);
        if (score == null || !text.Contains(Pending)) return;
        try { Bind(Get(score), new Binding(sprite, text, true)); }
        catch (Exception e) { Console.WriteLine("[Local pp tooltip] " + e.Message); }
    }

    private sealed class Entry(string preferences)
    {
        internal readonly string Preferences = preferences;
        internal CalculationResult? Result;
        internal DateTime RetryAfter;
        internal readonly List<Binding> Bindings = new();
    }
    private sealed class Binding(object sprite, string template, bool tooltip)
    {
        internal readonly WeakReference<object> Sprite = new(sprite);
        internal readonly string Template = template;
        internal readonly bool Tooltip = tooltip;
    }
}
