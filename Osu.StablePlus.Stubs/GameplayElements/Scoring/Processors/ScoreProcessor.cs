using JetBrains.Annotations;
using Osu.StablePlus.Utils.Lazy;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Stubs.GameplayElements.Scoring.Processors;

/// <summary>
///     Original: <c>osu.GameplayElements.Scoring.Processors.ScoreProcessor</c>
///     b20240123: <c>#=zBbxc56nwToQ2q_6LjFIHXSZoq$I8pyNIyxBZVi76rJHE</c>
/// </summary>
[PublicAPI]
public static class ScoreProcessor
{
    /// <summary>
    ///     Original: <c>Add(ScoreChange change)</c>
    ///     b20240123: <c>#=zJdXS36o=</c>
    /// </summary>
    [Stub]
    public static readonly LazyMethod AddScoreChange = LazyMethod.ByPartialSignature(
        "osu.GameplayElements.Scoring.Processors.ScoreProcessor::Add(ScoreChange)",
        new[]
        {
            Brfalse_S,
            Ldarg_1,
            Ldfld,
            Ldc_I4_0,
            Ble_S,
            Ldarg_1,
            Dup,
            Ldfld,
            Ldc_I4_1,
            Add,
            Stfld,
            Br_S,
            Ldarg_1,
        }
    );

}
