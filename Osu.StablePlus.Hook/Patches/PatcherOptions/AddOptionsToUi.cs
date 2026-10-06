using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Utils.Extensions;
using static Osu.StablePlus.Hook.Patches.CustomStrings.CustomStrings;

namespace Osu.StablePlus.Hook.Patches.PatcherOptions;

/// <summary>
///     Add this patcher's options to the options menu at the bottom.
/// </summary>
[OsuPatch]
[HarmonyPatch]
[UsedImplicitly]
internal static class AddOptionsToUi
{
    [UsedImplicitly]
    [HarmonyTargetMethod]
    private static MethodBase Target() => Options.InitializeOptions.Reference;

    [UsedImplicitly]
    [HarmonyPostfix]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private static void After(object __instance) =>
        Options.Add.Invoke(__instance, [CreatePatcherCategoryOption()]);

    private static object CreatePatcherCategoryOption()
    {
        // https://fontawesome.com/icons/eye-dropper?f=classic&s=solid
        const int fontAwesomeSyringe = 0xF1FB;

        var productString = AddOsuString("OsuStablePlus", Product.Name);
        var categoryChildren = new[]
        {
            CreatePatcherSectionOption(),
            CreatePatchOptionSpacer(2),
            CreatePatcherVersionOption(),
        }.ToType(OptionElement.Class.Reference);

        var category = OptionCategory.Constructor.Invoke([productString, fontAwesomeSyringe]);
        OptionElement.SetChildren.Invoke(category, [categoryChildren]);

        return category;
    }

    private static object CreatePatcherSectionOption()
    {
        var patcherString = AddOsuString("StablePlusSettings", "Settings");

        // Collect the various options from patches into OptionElement[]
        var sectionChildren = CollectOptions()
            .ToArray().ToType(OptionElement.Class.Reference);

        var keywords = new[] { "stable+", "stable plus", "patcher", "injector", "DT", "NC", "HT", "DA", "MR", "speed", "rate", "pitch", "stars", "score", "scoring", "RX", "AP", "Relax", "Autopilot" };
        var section = OptionSection.Constructor.Invoke([patcherString, keywords]);
        OptionSection.SetChildren.Invoke(section, [sectionChildren]);

        return section;
    }

    private static object CreatePatcherVersionOption()
    {
        return OptionVersion.Constructor.Invoke([$"{Product.Name} v{Product.Version}"]);
    }

    private static IEnumerable<object> CollectOptions()
    {
        // Collect all classes extending CustomOptions and initialize them to call CreateOptionsSprites()
        var options = Hook.PatchOptions
            .SelectMany(opts => opts.CreateOptions());

        return options;
    }

    private static object CreatePatchOptionSpacer(int lines = 1) => OptionText.Constructor.Invoke([
        /* title: */ new string('\n', lines),
        /* onClick: */ null,
    ]);
}
