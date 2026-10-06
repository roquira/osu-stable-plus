using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.GameplayElements.Beatmaps;
using Osu.StablePlus.Utils.IL;
using Osu.StablePlus.Utils.Lazy;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Stubs.GameModes.Play;

[PublicAPI]
public static class Player
{
    [Stub]
    public static readonly LazyMethod ResetScore = new("Player::ResetScore(Beatmap, bool)", () =>
        Class!.Reference.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m =>
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { Beatmap.Class.Reference, typeof(bool) }) &&
            MethodReader.GetInstructions(m).Any(i => i.Opcode == Stsfld && Equals(i.Operand, CurrentScore!.Reference))));

    [Stub]
    public static readonly LazyField<object?> ReplayScore = new("InputManager::replayScore", () =>
        MethodReader.GetInstructions(ResetScore.Reference).Where(i => i.Opcode == Ldsfld)
            .Select(i => i.Operand).OfType<FieldInfo>().First(f => f.FieldType == Score.Class.Reference && f != CurrentScore!.Reference));

    [Stub]
    public static readonly LazyMethod<bool> IsReplay = new("InputManager::get_ReplayMode()", () =>
        MethodReader.GetInstructions(ResetScore.Reference).Select(i => i.Operand).OfType<MethodInfo>()
            .First(m => m.DeclaringType == ReplayScore.Reference.DeclaringType && m.IsStatic &&
                        m.ReturnType == typeof(bool) && m.GetParameters().Length == 0));

    /// <summary>
    ///     Original: <c>osu.GameModes.Play.Player</c>
    ///     b20240124: <c>#=zOTWUr4vq60U15SRmD_JItyatbhdR</c>
    /// </summary>
    [Stub]
    public static readonly LazyType Class = new(
        "osu.GameModes.Play.Player",
        () => GetAllowDoubleSkip!.Reference.DeclaringType!
    );

    /// <summary>
    ///     Original: <c>get_AllowDoubleSkip()</c> (property getter)
    ///     b20240124: <c>#=zp29IlAJ43g4WRArPQA==</c>
    /// </summary>
    [Stub]
    public static readonly LazyMethod GetAllowDoubleSkip = LazyMethod.ByPartialSignature(
        "osu.GameModes.Play.Player::get_AllowDoubleSkip()",
        [
            Neg,
            Stloc_0,
            Ldarg_0,
            Isinst,
            Brtrue_S,
            Ldsfld,
            Ldloc_0,
            Call,
            Brtrue_S,
            Ldc_I4_0,
            Br_S,
        ]
    );

    /// <summary>
    ///     Original: <c>OnLoadComplete(bool success)</c>
    ///     b20240124: <c>#=zXb_K4cZvV$uy</c>
    /// </summary>
    [Stub]
    public static readonly LazyMethod<bool> OnLoadComplete = LazyMethod<bool>.ByPartialSignature(
        "osu.GameModes.Play.Player::OnLoadComplete(bool)",
        [
            Br,
            Ldloc_S,
            Callvirt,
            Unbox_Any,
            Stloc_2,
            Ldsfld,
            Ldfld,
            Call,
        ]
    );

    /// <summary>
    ///     Original: <c>currentScore</c>
    ///     b20240124: <c>#=zF6h5l4j0$TfX</c>
    /// </summary>
    [Stub]
    public static readonly LazyField<object?> CurrentScore = new(
        "osu.GameModes.Play.Player::currentScore",
        () => Class.Reference
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(field => field.FieldType == Score.Class.Reference)
    );
}
