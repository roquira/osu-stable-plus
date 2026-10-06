using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Tests;

public partial class StableIntegrationTests
{
    [Test]
    public void LoadingKeyGuardOnlyConsumesInputForItsActiveDialog()
    {
        var active = typeof(PatchLoadingScreen).GetField("active", BindingFlags.Static | BindingFlags.NonPublic)!;
        var dialogField = typeof(PatchLoadingScreen).GetField("dialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var loader = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PatchLoadingScreen));
        var dialog = new object();
        dialogField.SetValue(loader, dialog);
        var previous = active.GetValue(null);
        try
        {
            active.SetValue(null, loader);
            var handled = false;
            Assert.That(PatchLoadingScreen.BlockKey(dialog, ref handled), Is.False);
            Assert.That(handled, Is.True);
            handled = false;
            Assert.That(PatchLoadingScreen.BlockKey(new object(), ref handled), Is.True);
            Assert.That(handled, Is.False);
            active.SetValue(null, null);
            Assert.That(PatchLoadingScreen.BlockKey(dialog, ref handled), Is.True);
            Assert.That(handled, Is.False);
        }
        finally { active.SetValue(null, previous); }
    }

    [Test]
    public void LoadingScreenUsesNativeBusySceneAndReprocessingSpinner()
    {
        Assert.That(Enum.GetNames(GameBase.Mode.Reference.FieldType), Does.Contain("Busy"));
        Assert.That(PatchLoadingScreen.Constructor, Is.Not.Null);
        Assert.That(PatchLoadingScreen.Key.ReturnType, Is.EqualTo(typeof(bool)));
        Assert.That(PatchLoadingScreen.Delay.GetParameters()[1].ParameterType, Is.EqualTo(typeof(int)));
        var spinner = PatchLoadingScreen.Spinner;
        Assert.That(spinner.IsStatic, Is.True);
        // Native reprocessing spinner must retain its looping rotation animation.
        Assert.That(MethodReader.GetInstructions(spinner).Any(i => Equals(i.Operand, 1500)), Is.True);
    }
    [Test]
    public void BusyModeBlocksOptionsWithoutTemporaryHarmonyWrapper()
    {
        var field = GameBase.Mode.Reference;
        var original = field.GetValue(null);
        var instance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
            Osu.StablePlus.Stubs.GameModes.Options.Options.Class.Reference);
        GC.SuppressFinalize(instance);
        try
        {
            field.SetValue(null, Enum.Parse(field.FieldType, "Busy"));
            Assert.That(Osu.StablePlus.Stubs.GameModes.Options.Options.GetCanExpand.Invoke(instance), Is.False);
            field.SetValue(null, Enum.Parse(field.FieldType, "Menu"));
            Assert.That(Osu.StablePlus.Stubs.GameModes.Options.Options.GetCanExpand.Invoke(instance), Is.True);
        }
        finally { field.SetValue(null, original); }
    }

    [Test]
    public void LoadingFadeInReleasesTheNativeBlackTransition()
    {
        var method = PatchLoadingScreen.FadeIn;
        var fields = MethodReader.GetInstructions(method).Select(i => i.Operand).OfType<FieldInfo>().Distinct().ToArray();
        var values = fields.Select(f => f.GetValue(null)).ToArray();
        var state = fields.Single(f => f.FieldType.IsEnum);
        var opacity = fields.Single(f => f.FieldType == typeof(double));
        Assert.That(PatchLoadingScreen.TransitionOpacity, Is.EqualTo(opacity));
        try
        {
            state.SetValue(null, Enum.ToObject(state.FieldType, 3)); // Native fully black / waiting state.
            opacity.SetValue(null, 100d);
            method.Invoke(null, null);
            Assert.That(Convert.ToInt32(state.GetValue(null)), Is.EqualTo(1)); // Native fade-in state.
        }
        finally { for (var i = 0; i < fields.Length; i++) fields[i].SetValue(null, values[i]); }
    }

}
