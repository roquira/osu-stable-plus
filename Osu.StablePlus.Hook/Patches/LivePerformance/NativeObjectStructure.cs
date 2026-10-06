using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.GameModes.Play.Rulesets;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

internal static class NativeObjectStructure
{
    private static readonly Type HitObject = Ruleset.OnIncreaseScoreHit.Reference.GetParameters()[3].ParameterType;
    internal static readonly FieldInfo End = MethodReader.GetInstructions(HitObject.GetMethod("CompareTo", new[] { typeof(int) }))
        .Select(i => i.Operand).OfType<FieldInfo>().Single();
    internal static readonly FieldInfo Start = MethodReader.GetInstructions(HitObject.GetMethod("CompareTo", new[] { HitObject }))
        .Select(i => i.Operand).OfType<FieldInfo>().Distinct().Single(f => f != End);
    private static readonly MethodInfo Pan = HitObject.GetMethods(DifficultyControl.All).Single(m => m.ReturnType == typeof(float) &&
        m.GetParameters().Length == 0 && MethodReader.GetInstructions(m).Any(i => i.Opcode == Ldc_R4 && Equals(i.Operand, 512f)));
    private static readonly FieldInfo Position = MethodReader.GetInstructions(Pan).Select(i => i.Operand).OfType<FieldInfo>()
        .Single(f => f.FieldType == Osu.StablePlus.Stubs.XNA.Vector2.Class.Reference);
    private static readonly Dictionary<string, double[][]> Cache = new();

    internal static double[][] Capture(object map, int mode, int mods)
    {
        // Reflection preserves difficulty; compare unreflected columns/positions
        // so stable's display-space coordinates do not enter this contract.
        mods &= ~(DifficultyControl.Flag | MirrorSettings.Flag);
        string path = Beatmap.GetBeatmapPath(map)!;
        string key = path + "/" + File.GetLastWriteTimeUtc(path).Ticks + "/" + mode + "/" + mods;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var structure = SelectionStars.ReadNativeStructure(map, mods, mode, objects => objects
            .Where(o => mode != 1 || Convert.ToInt32(Start.GetValue(o)) == Convert.ToInt32(End.GetValue(o))).Select(o =>
        {
            double position = 0;
            if (mode == 2) position = Osu.StablePlus.Stubs.XNA.Vector2.X.Get(Position.GetValue(o));
            if (mode == 3)
            {
                // The note constructor records the original column before it
                // applies native Mirror or the player's column order.
                var type = o.GetType(); ConstructorInfo? constructor = null;
                for (; type != null && constructor == null; type = type.BaseType)
                    constructor = type.GetConstructors(DifficultyControl.All).SingleOrDefault(c => c.GetParameters().Length == 4 &&
                        c.GetParameters()[0].ParameterType == DifficultyControl.Timing.DeclaringType && c.GetParameters()[1].ParameterType == typeof(int));
                if (constructor == null) throw new InvalidOperationException("Cannot verify native mania columns.");
                var il = PatchProcessor.GetOriginalInstructions(constructor).ToArray();
                var field = (FieldInfo)il.Select((i, n) => new { i, n }).First(x => x.i.opcode == Stfld && x.n > 0 && il[x.n - 1].IsLdarg(2)).i.operand;
                position = Convert.ToInt32(field.GetValue(o));
            }
            return new[] { Convert.ToDouble(Start.GetValue(o)), Convert.ToDouble(End.GetValue(o)), position };
        }).ToArray());
        if (Cache.Count >= 16) Cache.Clear();
        Cache[key] = structure;
        return structure;
    }
}
