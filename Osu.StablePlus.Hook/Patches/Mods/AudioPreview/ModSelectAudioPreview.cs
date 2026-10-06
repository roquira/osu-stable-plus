using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;

namespace Osu.StablePlus.Hook.Patches.Mods.AudioPreview;

/// <summary>
///     Hooks the place where ModButtons get updates in the mod selection menu to apply audio effects.
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
internal class ModSelectAudioPreview
{
    [UsedImplicitly]
    [HarmonyTargetMethod]
    private static MethodBase Target() => ModSelection.UpdateMods.Reference;

    [UsedImplicitly]
    [HarmonyPostfix]
    private static void After(object __instance)
    {
        CustomRateOptions.ObserveMods(Osu.StablePlus.Stubs.GameplayElements.Scoring.ModManager.ModStatus.Get());
        if (ModMenuRateControl.Panels.TryGetValue(__instance, out var panel)) panel.Refresh();
        if (!AudioPreviewOptions.Enabled.Value)
            return;

        ModAudioEffects.ApplyModEffects();
    }
}
