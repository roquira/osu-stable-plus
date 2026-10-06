using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Osu.StablePlus.Stubs.GameModes.Select;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Stubs.XNA;
using Osu.StablePlus.Utils;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Structural bindings to stable's native dialog controls; no obfuscated names.</summary>
internal static class NativeModMenu
{
    internal const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    internal static readonly Type Menu = ModSelection.UpdateMods.Reference.DeclaringType!;
    internal static readonly Type Dialog = Menu.BaseType!;
    private static readonly Type Vector = Vector2.Class.Reference;
    private static readonly Type Colour = Color.White.GetType();
    internal static readonly MethodInfo AddButton = Dialog.GetMethods(Members).Single(m => Parameters(m,
        typeof(string), Colour, typeof(EventHandler), typeof(bool), typeof(bool), typeof(bool)));
    internal static readonly MethodInfo Dispose = Dialog.GetMethods(Members).Single(m =>
        m.IsFamily && m.IsVirtual && Parameters(m, typeof(bool)));
    internal static readonly FieldInfo Manager = Dialog.GetFields(Members).Single(f => f.FieldType == SpriteManager.Class.Reference);

    private static readonly ConstructorInfo[] Constructors = OsuAssembly.Types
        .SelectMany(t => t.GetConstructors(Members)).ToArray();
    internal static readonly ConstructorInfo Slider = Constructors.Single(c => Parameters(c,
        SpriteManager.Class.Reference, typeof(double), typeof(double), typeof(double), Vector, typeof(int)));
    internal static readonly MethodInfo SliderSet = Slider.DeclaringType!.GetMethods(Members).Single(m =>
        Parameters(m, typeof(double), typeof(bool), typeof(bool)));
    internal static readonly FieldInfo SliderValue = MethodReader.GetInstructions(SliderSet)
        .Where(i => i.Opcode == Stfld).Select(i => i.Operand).OfType<FieldInfo>().Single(f => f.FieldType == typeof(double));
    internal static readonly FieldInfo SliderChanged = Slider.DeclaringType!.GetFields(Members)
        .Single(f => typeof(Delegate).IsAssignableFrom(f.FieldType));
    internal static readonly MethodInfo SliderVisible = Slider.DeclaringType!.GetMethods(Members).Single(m =>
        Parameters(m, typeof(bool)) && FadeMethods(m).Length == 2);
    private static readonly MethodInfo[] Fades = FadeMethods(SliderVisible);
    internal static readonly MethodInfo Open = Dialog.GetMethods(Members).Single(m =>
        m.IsVirtual && m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
        MethodReader.GetInstructions(m).Select(i => i.Operand).OfType<MethodInfo>().Any(c =>
            c.DeclaringType == Fades[0].DeclaringType && Parameters(c, typeof(int)) && c.ReturnType != typeof(void)));
    internal static readonly FieldInfo IsOpen = MethodReader.GetInstructions(Open)
        .Where(i => i.Opcode == Stfld).Select(i => i.Operand).OfType<FieldInfo>().Last(f => f.FieldType == typeof(bool));
    internal static readonly FieldInfo FocusedDialog = Osu.StablePlus.Stubs.Root.GameBase.Mode.Reference.DeclaringType!
        .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(f => f.FieldType == Dialog);

    internal static void RestoreKeyboardFocus(object menu)
    {
        if ((bool)IsOpen.GetValue(menu) && FocusedDialog.GetValue(null) == null)
        {
            FocusedDialog.SetValue(null, menu);
            Console.WriteLine("[Mod menu] Restored keyboard focus to the open mod dialog.");
        }
    }

    private static MethodInfo[] FadeMethods(MethodInfo method) => MethodReader.GetInstructions(method)
        .Select(i => i.Operand).OfType<MethodInfo>().Where(m => m.GetParameters().Length == 3 &&
            m.GetParameters()[0].ParameterType == typeof(int) && m.GetParameters()[1].ParameterType.IsEnum &&
            m.GetParameters()[2].ParameterType == typeof(int)).Distinct().ToArray();

    internal static void SetVisible(object sprite, bool visible)
    {
        var method = Fades[visible ? 0 : 1];
        method.Invoke(sprite, [0, Enum.ToObject(method.GetParameters()[1].ParameterType, 0), 0]);
    }
    internal static readonly MethodInfo SliderKey = Slider.DeclaringType!.GetMethods(Members).Single(m =>
        m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.FullName == "Microsoft.Xna.Framework.Input.Keys");
    internal static readonly FieldInfo SliderKeyboard = MethodReader.GetInstructions(SliderKey)
        .Where(i => i.Opcode == Ldfld).Select(i => i.Operand).OfType<FieldInfo>().First(f => f.FieldType == typeof(bool));
    internal static readonly FieldInfo SliderStep = MethodReader.GetInstructions(SliderKey)
        .Where(i => i.Opcode == Ldfld).Select(i => i.Operand).OfType<FieldInfo>().Distinct()
        .Single(f => f.FieldType == typeof(double) && f != SliderValue);
    internal static readonly ConstructorInfo Checkbox = Constructors.Single(c => Parameters(c,
        typeof(string), Vector, typeof(float), typeof(bool)));
    internal static readonly FieldInfo CheckboxChanged = Checkbox.DeclaringType!.GetFields(Members)
        .Single(f => typeof(Delegate).IsAssignableFrom(f.FieldType));
    private static readonly ConstructorInfo Text = pText.SetText.Reference.DeclaringType!.GetConstructors(Members)
        .Single(c => Parameters(c, typeof(string), typeof(float), Vector, Vector, typeof(float), typeof(bool), Colour, typeof(bool)));
    internal static readonly ConstructorInfo ModIcon = MethodReader.GetInstructions(Menu.GetConstructors(Members).Single())
        .Select(i => i.Operand).OfType<ConstructorInfo>().First(c => c.GetParameters().Length == 2 &&
            c.GetParameters()[1].ParameterType == Osu.StablePlus.Stubs.Root.Mods.Type.Reference.MakeArrayType());
    internal static readonly FieldInfo ModTooltip = MethodReader.GetInstructions(ModIcon)
        .Where(i => i.Opcode == Stfld).Select(i => i.Operand).OfType<FieldInfo>()
        .Distinct().Single(f => f.FieldType == typeof(string));
    internal static readonly ConstructorInfo Sprite = MethodReader.GetInstructions(ModIcon)
        .Select(i => i.Operand).OfType<ConstructorInfo>().First(c => c.GetParameters().Length == 9);
    internal static readonly MethodInfo LoadTexture = Sprite.GetParameters()[0].ParameterType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(m => Parameters(m, typeof(byte[])) && m.ReturnType == Sprite.GetParameters()[0].ParameterType);
    private static readonly MethodInfo Click = MethodReader.GetInstructions(ModIcon).Select(i => i.Operand).OfType<MethodInfo>()
        .Where(m => Parameters(m, typeof(EventHandler))).Distinct().Skip(1).First();
    private static readonly FieldInfo Input = MethodReader.GetInstructions(ModIcon).Where(i => i.Opcode == Stfld)
        .Select(i => i.Operand).OfType<FieldInfo>().First(f => f.FieldType == typeof(bool));
    private static readonly FieldInfo Scale = (FieldInfo)MethodReader.GetInstructions(Sprite)
        .SkipWhile(i => !(i.Opcode == Ldc_R4 && Equals(i.Operand, 1f))).Skip(1).First().Operand;
    internal static readonly MethodInfo ModPosition = Menu.GetMethods(Members).Single(m =>
        m.ReturnType == Vector && Parameters(m, typeof(int), typeof(int)));
    internal static readonly MethodInfo RegisterMod = Menu.GetMethods(Members).Single(m => Parameters(m, ModIcon.DeclaringType!));
    internal static readonly MethodInfo ModState = ModIcon.DeclaringType!.GetMethods(Members).Single(m =>
        Parameters(m, typeof(bool), Osu.StablePlus.Stubs.Root.Mods.Type.Reference, typeof(bool)));
    internal static readonly FieldInfo ActiveMod = ModIcon.DeclaringType!.GetFields(Members).Single(f => f.FieldType == Osu.StablePlus.Stubs.Root.Mods.Type.Reference);
    internal static readonly MethodInfo ModTexture = MethodReader.GetInstructions(ModIcon).Select(i => i.Operand)
        .OfType<MethodInfo>().First(m => m.IsStatic && m.ReturnType == Sprite.GetParameters()[0].ParameterType);
    internal static readonly FieldInfo TextureResolution = LoadTexture.DeclaringType!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(m => m.GetMethodBody() != null).SelectMany(MethodReader.GetInstructions).Where(i => i.Opcode == Stfld)
        .Select(i => i.Operand).OfType<FieldInfo>().Distinct().Single(f => f.DeclaringType == LoadTexture.DeclaringType && f.FieldType == typeof(int));
    private static object? difficultyTexture;
    internal static object DifficultyTexture()
    {
        if (difficultyTexture != null) return difficultyTexture;
        using var stream = typeof(NativeModMenu).Assembly.GetManifestResourceStream("Osu.StablePlus.Hook.Resources.selection-mod-difficultyadjust.png")!;
        using var copy = new System.IO.MemoryStream();
        stream.CopyTo(copy);
        difficultyTexture = LoadTexture.Invoke(null, [copy.ToArray()]);
        TextureResolution.SetValue(difficultyTexture, 2);
        return difficultyTexture;
    }
    internal static object AddDifficultyMod(object menu)
        => AddMod(menu, DifficultyControl.Flag, 0);
    internal static object AddMod(object menu, int flag, int column)
    {
        var flags = Array.CreateInstance(Osu.StablePlus.Stubs.Root.Mods.Type.Reference, 1);
        flags.SetValue(Enum.ToObject(Osu.StablePlus.Stubs.Root.Mods.Type.Reference, flag), 0);
        var control = ModIcon.Invoke([ModPosition.Invoke(menu, [3, column]), flags]);
        SetModTooltip(control, flag);
        ModState.Invoke(control, [(Osu.StablePlus.Stubs.GameplayElements.Scoring.ModManager.ModStatus.Get() & flag) != 0, flags.GetValue(0), false]);
        RegisterMod.Invoke(menu, [control]);
        return control;
    }
    internal static void SetModTooltip(object control, int flag) =>
        ModTooltip.SetValue(GetSprites(control).First(), flag == DifficultyControl.Flag
            ? "Override a beatmap's difficulty settings." : "Flip objects on the chosen axes.");
    internal static void SyncDifficultyMod(object control, bool force = false)
    {
        var enabled = DifficultyControl.Enabled;
        if (!force && (Convert.ToInt32(ActiveMod.GetValue(control)) != 0) == enabled) return;
        ModState.Invoke(control, [enabled, Enum.ToObject(Osu.StablePlus.Stubs.Root.Mods.Type.Reference, DifficultyControl.Flag), false]);
    }

    internal static object AddImage(object manager, string resource, object position, float scale, EventHandler click)
    {
        using var stream = typeof(NativeModMenu).Assembly.GetManifestResourceStream("Osu.StablePlus.Hook.Resources." + resource)!;
        var bytes = new byte[stream.Length];
        var offset = 0;
        while (offset < bytes.Length) { var count = stream.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new System.IO.EndOfStreamException(); offset += count; }
        var texture = LoadTexture.Invoke(null, [bytes]);
        var args = Sprite.GetParameters();
        var sprite = Sprite.Invoke([texture, Enum.ToObject(args[1].ParameterType, 6),
            Enum.Parse(args[2].ParameterType, "TopLeft"), Enum.ToObject(args[3].ParameterType, 0),
            position, 0.996f, true, Color.White, null]);
        Scale.SetValue(sprite, scale);
        Input.SetValue(sprite, true);
        Click.Invoke(sprite, [click]);
        SpriteManager.Add.Invoke(manager, [sprite]);
        return sprite;
    }

    internal static void SetInteractive(object sprite, bool enabled) => Input.SetValue(sprite, enabled);
    internal static bool IsInteractive(object sprite) => (bool)Input.GetValue(sprite);
    internal static void OnClick(object sprite, EventHandler click) { Input.SetValue(sprite, true); Click.Invoke(sprite, [click]); }
    private static readonly MethodInfo Width = MethodReader.GetInstructions(AddButton)
        .Select(i => i.Operand).OfType<MethodInfo>().Distinct().Single(m =>
            m.ReturnType == typeof(int) && m.GetParameters().Length == 0 && !m.DeclaringType!.IsGenericType);
    internal static readonly FieldInfo Window = MethodReader.GetInstructions(AddButton)
        .Where(i => i.Opcode == Ldsfld).Select(i => i.Operand).OfType<FieldInfo>().Distinct()
        .Single(f => f.FieldType == Width.DeclaringType);

    internal static float GetWidth() => (int)Width.Invoke(Window.GetValue(null), null);
    internal static object Point(float x, float y) => Vector2.Constructor.Invoke([x, y]);

    internal static object AddText(object manager, string text, float size, float x, float y)
    {
        var sprite = Text.Invoke([text, size, Point(x, y), Point(0, 0), 0.995f, true, Color.White, false]);
        SpriteManager.Add.Invoke(manager, [sprite]);
        return sprite;
    }

    internal static IEnumerable<object> GetSprites(object control)
    {
        var sprites = control.GetType().GetFields(Members).Single(f => f.FieldType.IsGenericType &&
            f.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
            Sprite.DeclaringType!.IsAssignableFrom(f.FieldType.GetGenericArguments()[0]));
        return ((IEnumerable)sprites.GetValue(control)).Cast<object>();
    }

    internal static void Bind(FieldInfo field, object control, Delegate callback) =>
        field.SetValue(control, Delegate.CreateDelegate(field.FieldType, callback.Target, callback.Method));

    private static bool Parameters(MethodBase method, params Type[] types) =>
        method.GetParameters().Select(p => p.ParameterType).SequenceEqual(types);
}
