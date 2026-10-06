using System;
using Osu.StablePlus.Performance;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Osu.StablePlus.Hook.Patches.LivePerformance;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Debounced official ruleset stars. Native standard calculation retained for regression checks.</summary>
internal static class SelectionStars
{
    internal static readonly MethodInfo CalculateMethod = Beatmap.Class.Reference.Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(DifficultyControl.All)).Single(m => m.ReturnType == typeof(double) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] {
                Osu.StablePlus.Stubs.Root.Mods.Type.Reference, typeof(Dictionary<string, string>), DifficultyControl.Timing.DeclaringType! }));
    private static readonly Type Calculator = CalculateMethod.DeclaringType!;
    private static readonly MethodInfo Factory = Beatmap.Class.Reference.GetMethods(DifficultyControl.All)
        .Single(m => m.ReturnType == Calculator && m.GetParameters().Length == 1);
    internal static readonly FieldInfo MapField = Calculator.GetFields(DifficultyControl.All)
        .Single(f => f.FieldType == Beatmap.Class.Reference);
    internal static readonly MethodInfo SetRate = Calculator.GetMethods(DifficultyControl.All).Single(m =>
        m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
        MethodReader.GetInstructions(m).Any(i => (i.Opcode == Ldc_I4 || i.Opcode == Ldc_I4_S) && Convert.ToInt32(i.Operand) == 150));
    internal static readonly FieldInfo RateField = MethodReader.GetInstructions(SetRate)
        .Where(i => i.Opcode == Stfld).Select(i => i.Operand).OfType<FieldInfo>().Single();
    private static readonly MethodInfo[] CalculationCalls = MethodReader.GetInstructions(CalculateMethod)
        .Select(i => i.Operand).OfType<MethodInfo>().ToArray();
    private static readonly MethodInfo Prepare = CalculationCalls.Single(m => m.DeclaringType == Calculator &&
        m.ReturnType == typeof(void) && m.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual(new[] { Osu.StablePlus.Stubs.Root.Mods.Type.Reference }));
    private static readonly MethodInfo Compute = CalculationCalls.Single(m => m.DeclaringType == Calculator &&
        m.ReturnType == typeof(double) && m.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual(new[] { typeof(Dictionary<string, string>) }));
    private static readonly FieldInfo HitObjects = MethodReader.GetInstructions(CalculateMethod)
        .Select(i => i.Operand).OfType<FieldInfo>().Distinct().Single(f => f.DeclaringType == Calculator &&
            f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>));
    private static readonly MethodInfo Dispose = Calculator.GetMethods(DifficultyControl.All).Single(m =>
        m.IsPublic && m.IsVirtual && m.ReturnType == typeof(void) && m.GetParameters().Length == 0);
    private static readonly object Sync = new();
    private static readonly Dictionary<string, CalculationResult?> Cache = new();
    private static readonly Dictionary<string, DateTime> Retry = new();
    private static Request? pending;
    private static string? runningKey;
    private static bool working;
    private static int revision;
    [ThreadStatic] internal static object? CalculatingMap;
    internal static int Revision => System.Threading.Volatile.Read(ref revision);

    internal static string Get(object map, int mods, double rate, DifficultySettings? difficulty)
        => GetValues(map, mods, rate, difficulty).Stars;

    internal static DisplayValues GetValues(object map, int mods, double rate, DifficultySettings? difficulty)
    {
        var mode = StandardModLayout.ModeFor(map);
        var source = StandardModLayout.NativeModeOf(map);
        // Capture preferences before caching/queuing so changes cannot reuse a different calculation.
        mods = mode != 3 ? StarRatingOptions.CalculationMods(mods) : mods;
        var path = Beatmap.GetBeatmapPath(map);
        if (path == null || !File.Exists(path)) return DisplayValues.Failed;
        var mirror = mode == 0 && (mods & MirrorSettings.Flag) != 0 ? MirrorSettings.Selected.ToString() : null;
        var key = CalculationRequest.ProtocolVersion + "/" + CalculationRequest.EngineVersion + "/" + mode + "/" + source + "/" + path + "/" + File.GetLastWriteTimeUtc(path).Ticks + "/" + mods + "/" + mirror + "/" + rate.ToString("R", CultureInfo.InvariantCulture) +
            "/" + string.Join("/", difficulty?.Values.Select(v => v?.ToString("R", CultureInfo.InvariantCulture)) ?? Enumerable.Empty<string>());
        lock (Sync)
        {
            if (Cache.TryGetValue(key, out var result))
            {
                if (result != null || !Retry.TryGetValue(key, out var retry) || DateTime.UtcNow < retry)
                    return result == null ? DisplayValues.Failed : new DisplayValues(
                        result.Stars.ToString("0.##", CultureInfo.InvariantCulture), PerformanceRequests.Number(result.Pp));
                Cache.Remove(key); Retry.Remove(key);
            }
            if (runningKey != key && pending?.Key != key) pending = new Request(path, mods, rate, difficulty, mirror, key, mode, source);
            if (!working) { working = true; Task.Run(Process); }
        }
        return DisplayValues.Pending;
    }

    private static async Task Process()
    {
        while (true)
        {
            // Coalesce rapid selection/slider changes without blocking the UI.
            await Task.Delay(150).ConfigureAwait(false);
            Request request;
            lock (Sync)
            {
                if (pending == null) { working = false; runningKey = null; return; }
                request = pending;
                pending = null;
                runningKey = request.Key;
            }
            CalculationResult? result;
            try
            {
                var calculated = await PerformanceClient.CalculateAsync(new CalculationRequest
                {
                    MapPath = request.Path,
                    Mode = request.Mode,
                    SourceMode = request.SourceMode,
                    Mods = request.Mods,
                    Rate = request.Rate,
                    Difficulty = request.Difficulty?.Values.ToArray() ?? new double?[4],
                    Maximum = true,
                    Mirror = request.Mirror
                }).ConfigureAwait(false);
                if (calculated.Error != null) throw new IOException(calculated.Error);
                result = calculated;
            }
            catch (Exception e)
            {
                Console.WriteLine("[Selection details] Star calculation failed: " + e);
                result = null;
            }
            lock (Sync)
            {
                if (Cache.Count >= 128) { Cache.Clear(); Retry.Clear(); }
                Cache[request.Key] = result;
                if (result == null) Retry[request.Key] = DateTime.UtcNow.AddSeconds(5);
                runningKey = null;
                Interlocked.Increment(ref revision);
            }
        }
    }

    internal readonly struct DisplayValues(string stars, string maxPp)
    {
        internal static readonly DisplayValues Pending = new("…", "…");
        internal static readonly DisplayValues Failed = new("?", "?");
        internal readonly string Stars = stars;
        internal readonly string MaxPp = maxPp;
    }

    internal static double Calculate(object map, int mods, double rate, DifficultySettings? difficulty)
    {
        // Requests are captured while selected as standard. A later mode switch
        // must not change an already queued calculation's ruleset.
        if (StandardModLayout.NativeModeOf(map) != 0)
            throw new InvalidOperationException("Custom star calculation only supports osu!standard maps.");
        var calculator = Factory.Invoke(map, [Enum.ToObject(Factory.GetParameters()[0].ParameterType, 0)]);
        var previousMap = CalculatingMap;
        try
        {
            var copy = MapField.GetValue(calculator) ?? throw new IOException("Unable to load selected beatmap for difficulty calculation.");
            if (ReferenceEquals(copy, map)) throw new InvalidOperationException("Expected a private beatmap copy.");
            CalculatingMap = copy;
            if (difficulty != null)
                for (var i = 0; i < 4; i++)
                    if (difficulty.Values[i].HasValue) DifficultyControl.Fields[i].SetValue(copy, (float)difficulty.Values[i]!.Value);
            // Mirror is a reflection (preserves spacing and timing); remove its mania flag and DA's private flag.
            var nativeMods = mods & ~(DifficultyControl.Flag | MirrorSettings.Flag);
            // A fresh calculator follows the native entry point's prepare/compute path.
            // Set its rate after preparation, before difficulty objects are built. This
            // avoids detouring stable's rate helper (which can fail in the running game).
            Prepare.Invoke(calculator, [Enum.ToObject(Osu.StablePlus.Stubs.Root.Mods.Type.Reference, nativeMods)]);
            if (HitObjects.GetValue(calculator) == null) return 0;
            RateField.SetValue(calculator, rate);
            return (double)Compute.Invoke(calculator, [null]);
        }
        finally { CalculatingMap = previousMap; Dispose.Invoke(calculator, null); }
    }

    internal static T ReadNativeStructure<T>(object map, int mods, int mode, Func<object[], T> read)
    {
        var calculator = Factory.Invoke(map, [Enum.ToObject(Factory.GetParameters()[0].ParameterType, mode)]);
        try
        {
            Prepare.Invoke(calculator, [Enum.ToObject(Osu.StablePlus.Stubs.Root.Mods.Type.Reference, mods)]);
            var objects = HitObjects.GetValue(calculator) as System.Collections.IEnumerable;
            var list = objects?.Cast<object>() ?? Enumerable.Empty<object>();
            if (mode == 2)
            {
                // The native catch difficulty-object iterator explicitly omits
                // bananas and tiny droplets. Use those same runtime types.
                var omitted = calculator.GetType().GetMethods(DifficultyControl.All)
                    .SelectMany(m => MethodReader.GetInstructions(m))
                    .Where(i => i.Opcode == Isinst).Select(i => i.Operand).OfType<Type>()
                    .Where(t => HitObjects.FieldType.GetGenericArguments()[0].IsAssignableFrom(t)).Distinct().ToArray();
                if (omitted.Length != 2) throw new InvalidOperationException("Cannot verify native catch conversion objects.");
                list = list.Where(o => !omitted.Any(t => t.IsInstanceOfType(o)));
            }
            return read(list.ToArray());
        }
        finally { Dispose.Invoke(calculator, null); }
    }

    private sealed class Request(string path, int mods, double rate, DifficultySettings? difficulty, string? mirror, string key, int mode, int sourceMode)
    {
        internal readonly int Mode = mode, SourceMode = sourceMode;
        internal readonly string Path = path;
        internal readonly int Mods = mods;
        internal readonly double Rate = rate;
        internal readonly string? Mirror = mirror;
        internal readonly DifficultySettings? Difficulty = difficulty;
        internal readonly string Key = key;
    }
}
