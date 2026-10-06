using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.GameModes.Play;
using Osu.StablePlus.Stubs.Graphics;
using Osu.StablePlus.Stubs.Graphics.Skinning;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Stubs.XNA;

namespace Osu.StablePlus.Hook.Patches.LivePerformance;

/// <summary>
///     Uses stable's skin score font and the original patcher's counter placement.
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
internal static class AddPerformanceToUi
{
    internal static FieldInfo AnchorField(int parameter)
    {
        var type = pSpriteText.Constructor.Reference.GetParameters()[parameter].ParameterType;
        for (var current = pText.SetText.Reference.DeclaringType; current != null; current = current.BaseType)
        {
            var matches = current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType == type).ToArray();
            if (matches.Length == 1) return matches[0];
        }
        throw new System.InvalidOperationException("Native text anchor field not found.");
    }
    [UsedImplicitly]
    [HarmonyTargetMethod]
    private static MethodBase Target() => ScoreDisplay.Constructor.Reference;

    [UsedImplicitly]
    [HarmonyPostfix]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private static void After(
        object __instance, // ScoreDisplay
        [HarmonyArgument(0)] object spriteManager, // SpriteManager
        [HarmonyArgument(1)] object position, // Vector2
        [HarmonyArgument(2)] bool alignRight,
        [HarmonyArgument(3)] float scale
    )
    {
        if (!PerformanceOptions.ShowPerformanceInGame.Value)
            return;

        Debug.WriteLine("Adding Performance Counter to ScoreDisplay", nameof(AddPerformanceToUi));

        var currentSkin = SkinManager.Current.Get();
        var performanceSprite = pSpriteText.Constructor.Invoke([
            "", SkinOsu.FontScore.Get(currentSkin), (float)SkinOsu.FontScoreOverlap.Get(currentSkin),
            alignRight ? Fields.TopRight : Fields.TopLeft,
            alignRight ? Origins.TopRight : Origins.TopLeft,
            Clocks.Game, Vector2.Constructor.Invoke([0f, 0f]), 0.95f, true, Color.White, true, SkinSource.ExceptBeatmap
        ]);
        // Arbitrary status messages cannot assume matching letter textures in the skin.
        var status = Osu.StablePlus.Hook.Patches.Mods.CustomRate.NativeModMenu.AddText(spriteManager, "", 10f, 0, 0);
        void SetEnum(int parameter, int value)
        {
            var field = AnchorField(parameter);
            field.SetValue(status, System.Enum.ToObject(field.FieldType, value));
        }
        SetEnum(3, alignRight ? Fields.TopRight : Fields.TopLeft);
        SetEnum(4, alignRight ? Origins.TopRight : Origins.TopLeft);
        var positionX = Vector2.X.Get(position) + 8f;
        var positionY = GetYOffset(Vector2.Y.Get(position), scale, __instance);
        var newPosition = Vector2.Constructor.Invoke([positionX, positionY]);
        pDrawable.Position.Set(performanceSprite, newPosition);
        pDrawable.Position.Set(status, newPosition);
        pDrawable.Scale.Set(status, scale);
        pDrawable.Scale.Set(performanceSprite, 0.50f);
        pSpriteText.TextConstantSpacing.Set(performanceSprite, true);
        pSpriteText.MeasureText.Invoke(performanceSprite);
        SpriteManager.Add.Invoke(spriteManager, [performanceSprite]);
        PerformanceDisplay.SetPerformanceCounter(performanceSprite, status);

        Debug.WriteLine("Added Performance Counter to ScoreDisplay", nameof(AddPerformanceToUi));
    }

    private static float GetYOffset(float baseYPosition, float scale, object scoreDisplay)
    {
        // Read the heights of both pSpriteTexts: s_Score, s_Accuracy
        var sprites = ScoreDisplay.Class.Reference
            .GetDeclaredFields()
            .Where(f => f.FieldType == pSpriteText.Class.Reference)
            .Select(f => f.GetValue(scoreDisplay));
        var spriteSizes = sprites
            .Where(s => s != null)
            .Select(s => pSpriteText.MeasureText.Invoke(s));
        var totalSpriteHeight = spriteSizes.Sum(v => Vector2.Y.Get(v)) * 0.58f * scale;

        // Preserve additional spacing between s_Score and s_Accuracy
        var additionalOffset = SkinManager.GetUseNewLayout.Invoke() ? 3f : 0f;

        return baseYPosition + totalSpriteHeight + additionalOffset;
    }
}
