using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Hook;
using Osu.StablePlus.Hook.Patches;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Tests;

public partial class StableIntegrationTests
{
    [Test]
    public void NativeSpriteTextureGetterIsNotPatchedOrReplaced()
    {
        var textureType = NativeModMenu.LoadTexture.ReturnType;
        var getter = NativeModMenu.Sprite.DeclaringType!.GetMethods(NativeModMenu.Members)
            .Single(m => m.IsVirtual && m.ReturnType == textureType && m.GetParameters().Length == 0);
        Assert.That(Harmony.GetPatchInfo(getter)?.Owners ?? Enumerable.Empty<string>(), Does.Not.Contain(harmony.Id));
        var sprite = FormatterServices.GetUninitializedObject(getter.DeclaringType!);
        var texture = FormatterServices.GetUninitializedObject(textureType);
        var field = MethodReader.GetInstructions(getter).Select(i => i.Operand).OfType<FieldInfo>().Single();
        field.SetValue(sprite, texture);
        Assert.That(getter.Invoke(sprite, null), Is.SameAs(texture));
        Assert.That(field.GetValue(sprite), Is.SameAs(texture));
        Assert.That(typeof(Settings).Assembly.GetManifestResourceNames(),
            Does.Not.Contain("Osu.StablePlus.Hook.Resources.menu-osu-stable-plus.png"));
    }

    [Test]
    public void LoadedOptionsAndNativeResetDefaultsAgreeWithFreshConfig()
    {
        var options = PatchOptions.CreateAllPatchOptions().ToArray();
        var previous = new Settings();
        foreach (var option in options) option.Save(previous);
        try
        {
            foreach (var option in options) option.Load(new Settings());
            var saved = new Settings();
            foreach (var option in options) option.Save(saved);
            SettingsDefaultsTests.AssertDefaults(saved);
            foreach (var option in options)
                foreach (var field in option.GetType().GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.FieldType == typeof(BindableWrapper<bool>))
                    {
                        var binding = (BindableWrapper<bool>)field.GetValue(null);
                        Assert.That(BindableBool.Default.Get(binding.Bindable), Is.EqualTo(binding.Value), field.Name);
                    }
                    else if (field.FieldType == typeof(BindableWrapper<int>))
                    {
                        var binding = (BindableWrapper<int>)field.GetValue(null);
                        Assert.That(new Bindable(typeof(int)).Default.Get(binding.Bindable), Is.EqualTo(binding.Value), field.Name);
                    }
                }
        }
        finally { foreach (var option in options) option.Load(previous); }
    }
}
