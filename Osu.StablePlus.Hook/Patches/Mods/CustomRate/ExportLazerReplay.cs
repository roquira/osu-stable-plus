using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

[OsuPatch, HarmonyPatch]
internal static class ExportLazerReplay
{
    [HarmonyTargetMethod]
    internal static MethodBase Target() => Score.Class.Reference.Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
        .Single(m => m.ReturnType == typeof(bool) &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { Score.Class.Reference, typeof(bool) }) &&
            MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, Score.WriteReplay.Reference)));

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(Score.WriteReplay.Reference))
            {
                var exportFlag = new CodeInstruction(Ldarg_1);
                exportFlag.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                yield return exportFlag;
                instruction.opcode = Call;
                instruction.operand = AccessTools.Method(typeof(ExportLazerReplay), nameof(Write));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Expected one replay write in score export.");
    }

    internal static void Write(object score, BinaryWriter writer, bool export)
    {
        Score.WriteReplay.Invoke(score, [writer]);
        if (!export || RulesetCompatibility.UnsupportedReplay(score) || (!CustomRateOptions.IsEnabled(RateControl.GetMods(score)) && RateControl.ForScore(score).Difficulty == null && RateControl.ForScore(score).Mirror == null)) return;
        writer.Flush();
        var stream = writer.BaseStream;
        if (!stream.CanRead || !stream.CanSeek || stream.Length > LazerReplayExport.MaxReplayBytes)
            throw new IOException("Cannot read back the exported replay.");
        stream.Position = 0;
        var bytes = new byte[checked((int)stream.Length)];
        var position = 0;
        while (position < bytes.Length)
        {
            var read = stream.Read(bytes, position, bytes.Length - position);
            if (read == 0) throw new EndOfStreamException();
            position += read;
        }
        // Finish conversion before replacing the file's contents.
        var converted = LazerReplayExport.Convert(bytes, RateControl.ForScore(score));
        stream.Position = 0;
        // Stable's derived writer overrides byte-array encoding; these are already
        // complete file bytes, so write directly to the underlying stream.
        stream.Write(converted, 0, converted.Length);
        stream.SetLength(converted.Length);
    }
}
