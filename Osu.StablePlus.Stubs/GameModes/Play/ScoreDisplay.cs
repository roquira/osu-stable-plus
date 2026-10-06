using JetBrains.Annotations;
using Osu.StablePlus.Utils.Lazy;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Stubs.GameModes.Play;

[PublicAPI]
public static class ScoreDisplay
{
    /// <summary>
    ///     Original: <c>osu.GameModes.Play.Components.ScoreDisplay</c>
    ///     b20240124: <c>#=z6dniqZasYGnUF21A3FQQhhWHV7POD$6AVg==</c>
    /// </summary>
    [Stub]
    public static readonly LazyType Class = new(
        "osu.GameModes.Play.Components.ScoreDisplay",
        () => Constructor!.Reference.DeclaringType!
    );

    /// <summary>
    ///     Original:
    ///     <c>
    ///         ScoreDisplay(SpriteManager spriteManager,
    ///         Vector2 position,
    ///         bool alignRight,
    ///         float scale,
    ///         bool showScore,
    ///         bool showAccuracy)
    ///     </c>
    ///     b20240124: Same as class
    /// </summary>
    [Stub]
    public static readonly LazyConstructor Constructor = LazyConstructor.ByPartialSignature(
        "osu.GameModes.Play.Components.ScoreDisplay::ScoreDisplay(SpriteManager, Vector2, bool, float, bool, bool)",
        [
            Conv_R4,
            Ldarg_3,
            Brtrue_S,
            Ldc_I4_6,
            Br_S,
            Ldc_I4_8,
            Ldarg_3,
            Brtrue_S,
            Ldc_I4_0,
            Br_S,
        ]
    );

}
