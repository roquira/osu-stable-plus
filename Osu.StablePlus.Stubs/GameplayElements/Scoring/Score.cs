using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Utils.Extensions;
using Osu.StablePlus.Utils.IL;
using Osu.StablePlus.Utils.Lazy;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Stubs.GameplayElements.Scoring;

[PublicAPI]
public static class Score
{
    [Stub]
    public static readonly LazyField<bool> IsLoadedReplay = new("Score::isReplay", () =>
    {
        var header = MethodReader.GetInstructions(ReadReplay!.Reference).Select(i => i.Operand)
            .OfType<MethodInfo>().First(m => m.DeclaringType == Class!.Reference && m.IsPublic &&
                m.GetParameters().Length == 1 && typeof(BinaryReader).IsAssignableFrom(m.GetParameters()[0].ParameterType));
        return MethodReader.GetInstructions(header).Where(i => i.Opcode == Stfld)
            .Select(i => i.Operand).OfType<FieldInfo>().First(f => f.FieldType == typeof(bool));
    });

    [Stub]
    public static readonly LazyMethod ReadReplay = new("Score::ReadReplay(SerializationReader, bool)",
        () => Class!.Reference.GetDeclaredMethods().Single(method =>
        {
            var args = method.GetParameters();
            return args.Length == 2 && typeof(BinaryReader).IsAssignableFrom(args[0].ParameterType)
                   && args[1].ParameterType == typeof(bool);
        }));

    [Stub]
    public static readonly LazyMethod WriteReplay = new("Score::WriteReplay(SerializationWriter)",
        () => Class!.Reference.GetDeclaredMethods().Single(method =>
            method.IsPublic && method.GetParameters().Length == 1 &&
            typeof(BinaryWriter).IsAssignableFrom(method.GetParameters()[0].ParameterType)));

    [Stub]
    public static readonly LazyField<byte[]?> ReplayData = new("Score::replayData",
        () => Class!.Reference.GetDeclaredFields().Single(field => field.FieldType == typeof(byte[])));

    [Stub]
    public static readonly LazyField<int> ReplayVersion = new("Score::replayVersion", () =>
    {
        var il = MethodReader.GetInstructions(ReadReplay.Reference).ToList();
        var index = il.FindIndex(i => i.Opcode == Ldc_I4 && Equals(i.Operand, 20140721));
        if (index < 1 || il[index - 1].Opcode != Ldfld)
            throw new InvalidOperationException("Cannot identify replay version field.");
        return (FieldInfo)il[index - 1].Operand;
    });

    [Stub]
    public static readonly LazyMethod GetReplayFilename = new("Score::get_ReplayFilename()", () =>
        Class!.Reference.GetDeclaredMethods().Single(method => method.ReturnType == typeof(string) &&
            MethodReader.GetInstructions(method).Any(i => i.Operand is MethodInfo call &&
                call.DeclaringType == typeof(DateTime) && call.Name == nameof(DateTime.ToFileTimeUtc))));

    /// <summary>
    ///     Original: <c>osu.GameplayElements.Scoring.Score</c>
    ///     b20240123: <c>#=zwswDPw49w3ZrQEjyKFYhq9$8W6WDMiSHDDrNV7k=</c>
    /// </summary>
    [Stub]
    public static readonly LazyType Class = new(
        "osu.GameplayElements.Scoring.Score",
        () => GetAccuracy!.Reference.DeclaringType!
    );

    /// <summary>
    ///     Original: <c>Score(string input, Beatmap beatmap)</c>
    ///     b20240123: <c>#=zwswDPw49w3ZrQEjyKFYhq9$8W6WDMiSHDDrNV7k=</c>
    /// </summary>
    [Stub]
    public static readonly LazyConstructor Constructor = new(
        "osu.GameplayElements.Scoring.Score::Score(string, Beatmap)",
        () => Class.Reference
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(ctor => ctor.GetParameters()
                .GetOrDefault(0, null)?
                .ParameterType == typeof(string)) // Find the only constructor with a "string" as the first parameter
    );

    /// <summary>
    ///     Original: <c>get_Accuracy()</c> (property getter)
    ///     b20240123: <c>#=zY_1A2REMae0xoSV0fA==</c>
    /// </summary>
    [Stub]
    public static readonly LazyMethod<float> GetAccuracy = LazyMethod<float>.ByPartialSignature(
        "osu.GameplayElements.Scoring.Score::get_Accuracy()",
        [
            Ldc_R4,
            Ret,
            Ldarg_0,
            Ldfld,
            Ldc_I4_S,
            Mul,
            Ldarg_0,
            Ldfld,
            Ldc_I4_S,
            Mul,
            Add,
        ]
    );

    /// <summary>
    ///     Original: <c>Beatmap</c>
    ///     b20240123: <c>#=zhcWn5UkrdlUu</c>
    /// </summary>
    [Stub]
    public static readonly LazyField<object?> Beatmap = new(
        "osu.GameplayElements.Scoring.Score::Beatmap",
        () => Class.Reference
            .GetRuntimeFields()
            .Single(inst => inst.FieldType == Beatmaps.Beatmap.Class.Reference)
    );

    /// <summary>
    ///     Original: <c>MaxCombo</c>
    ///     b20240123: <c>#=zkQ9fUTuRkHox</c>
    /// </summary>
    [Stub]
    public static readonly LazyField<int> MaxCombo = new(
        "osu.GameplayElements.Scoring.Score::MaxCombo",
        () =>
        {
            // Look for this: "this.MaxCombo = (int)Convert.ToUInt16(array[num++]);"
            var findMethod = AccessTools.Method(typeof(Convert), nameof(Convert.ToUInt16), [typeof(string)])!;

            var storeInstruction = MethodReader
                .GetInstructions(Constructor.Reference)
                .SkipWhile(inst => !findMethod.Equals(inst.Operand))
                .Skip(1)
                .First();

            Debug.Assert(storeInstruction.Opcode == Stfld);

            return (FieldInfo)storeInstruction.Operand;
        }
    );

    /// <summary>
    ///     Is of type <c>Obfuscated{Mods}</c>
    ///     Original: <c>EnabledMods</c>
    ///     b20240123: <c>#=zxL1NzqBwrqNU</c>
    /// </summary>
    [Stub]
    public static readonly LazyField<object> EnabledMods = new(
        "osu.GameplayElements.Scoring.Score::EnabledMods",
        () => Class.Reference
            .GetDeclaredFields()
            .Single(field => field.FieldType.IsGenericType &&
                             field.FieldType.GetGenericTypeDefinition() == Obfuscated.Generic.Class.Reference)
    );
}
