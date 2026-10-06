using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Utils.Extensions;
using Osu.StablePlus.Utils.Lazy;
using Osu.StablePlus.Utils.IL;
using System.Reflection;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Stubs.Root;

[PublicAPI]
public static class GameBase
{
    [Stub]
    public static readonly LazyField<int> Mode = new("osu.GameBase::Mode", () =>
        (FieldInfo)MethodReader.GetInstructions(GetModeCanReload!.Reference)
            .First(i => i.Opcode == Ldsfld).Operand);

    /// <summary>
    ///     Original: <c>osu.GameBase</c>
    ///     b20240123: <c>#=zduF3QmjgMG4eSc$fOQ==</c>
    /// </summary>
    [Stub]
    public static readonly LazyType Class = new(
        "osu.GameBase",
        () => GetModeCanReload!.Reference.DeclaringType
    );

    /// <summary>
    ///     Original: <c>get_ModeCanReload()</c>
    ///     b20240123: <c>#=zL6aRJUMxZO5fmlF9KQ==</c>
    /// </summary>
    [Stub]
    public static readonly LazyMethod<bool> GetModeCanReload = LazyMethod<bool>.ByPartialSignature(
        "osu.GameBase::get_ModeCanReload()",
        [
            Ldc_I4_7,
            Bgt_S,
            Ldloc_0,
            Ldc_I4_2,
            Beq_S,
            Ldloc_0,
            Ldc_I4_7,
            Beq_S,
            Br_S,
        ]
    );

    /// <summary>
    ///     Original: <c>softHandle(Exception e)</c>
    ///     b20240123: <c>#=z8BJOiJxSLUwM</c>
    /// </summary>
    [Stub]
    public static readonly LazyMethod SoftHandle = LazyMethod.ByPartialSignature(
        "osu.GameBase::softHandle(Exception)",
        [
            Ldarg_0,
            Isinst,
            Brtrue_S,
            Ldarg_0,
            Isinst,
            Brtrue_S,
            Ldarg_0,
            Isinst,
            Brfalse_S,
            Ldc_I4_8,
        ]
    );

    /// <summary>
    ///     Original: <c>Options</c>
    ///     b20240123: <c></c>
    /// </summary>
    [Stub]
    public static readonly LazyField<object> Options = new(
        "osu.GameBase::Options",
        () => Class.Reference
            .GetDeclaredFields()
            .First(field => field.FieldType == GameModes.Options.Options.Class.Reference)
    );

    /// <summary>
    ///     Original: <c>Scheduler</c>
    ///     This is the main thread scheduler.
    /// </summary>
    [Stub]
    public static readonly LazyField<object> Scheduler = new(
        "osu.GameBase::Scheduler",
        // There's two fields of type Scheduler, get the first one
        () => Class.Reference
            .GetDeclaredFields()
            .Where(field => field.FieldType == Helpers.Scheduler.Class.Reference)
            .FirstOrNull()!
    );
}
