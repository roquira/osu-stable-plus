using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Stubs.XNA;
using Osu.StablePlus.Utils;
using Osu.StablePlus.Utils.IL;
using static System.Reflection.Emit.OpCodes;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Reuse Visual Settings' primitives, without its gameplay/skin side effects.</summary>
internal static class NativeModDrawer
{
    private const BindingFlags Members = NativeModMenu.Members;
    private static readonly Type Vector = Vector2.Class.Reference;
    private static readonly Type Text = pText.SetText.Reference.DeclaringType!;
    internal static readonly Type VisualSettings = OsuAssembly.Types.Single(t =>
        t.BaseType == NativeModMenu.Dialog.BaseType &&
        t.GetFields(Members).Any(f => f.FieldType == NativeModMenu.Slider.DeclaringType));
    internal static readonly MethodInfo Initialize = VisualSettings.GetMethods(Members).Single(m =>
        MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, NativeModMenu.Slider)));
    private static readonly MethodInfo Update = VisualSettings.GetMethods(Members).Single(m =>
        m.IsPublic && m.IsVirtual && m.GetParameters().Length == 0 && m != Initialize);
    private static readonly MethodInfo[] PointerMethods = MethodReader.GetInstructions(Update).Select(i => i.Operand)
        .OfType<MethodInfo>().Where(m => m.IsStatic && m.ReturnType == Vector).ToArray();
    // Visual Settings reads the logical cursor twice (the Y bounds); raw pixels
    // are used for the initial movement threshold and the horizontal bounds.
    internal static readonly MethodInfo Pointer = PointerMethods.GroupBy(m => m).Single(g => g.Count() == 2).Key;
    internal static readonly MethodInfo Height = MethodReader.GetInstructions(Update).Select(i => i.Operand)
        .OfType<MethodInfo>().Distinct().Single(m => m.DeclaringType == NativeModMenu.Window.FieldType &&
            m.ReturnType == typeof(int) && m.GetParameters().Length == 0);
    private static readonly FieldInfo WhitePixel = MethodReader.GetInstructions(Initialize).Where(i => i.Opcode == Ldsfld)
        .Select(i => i.Operand).OfType<FieldInfo>().First(f => f.FieldType == NativeModMenu.Sprite.GetParameters()[0].ParameterType);
    private static readonly FieldInfo ScaleVector = MethodReader.GetInstructions(Initialize).Where(i => i.Opcode == Stfld)
        .Select(i => i.Operand).OfType<FieldInfo>().First(f => f.FieldType == Vector && f.DeclaringType == NativeModMenu.Sprite.DeclaringType!.BaseType);
    private static readonly MethodInfo Bold = MethodReader.GetInstructions(Initialize).Select(i => i.Operand)
        .OfType<MethodInfo>().First(m => m.DeclaringType == Text && m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType == typeof(bool));
    internal static readonly FieldInfo Position = MethodReader.GetInstructions(NativeModMenu.Sprite).Where(i => i.Opcode == Stfld)
        .Select(i => i.Operand).OfType<FieldInfo>().First(f => f.FieldType == Vector);
    private static readonly FieldInfo X = Vector.GetField("X")!, Y = Vector.GetField("Y")!;
    private static readonly MethodInfo Move = Position.DeclaringType!.GetMethods(Members).Single(m =>
        m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == Vector && m.GetParameters()[1].ParameterType == typeof(int));
    private static readonly MethodInfo MoveY = VisualSettings.GetMethods(Members)
        .Where(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == Position.DeclaringType)
        .SelectMany(MethodReader.GetInstructions).Select(i => i.Operand).OfType<MethodInfo>().Distinct().Single(m =>
            m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(float) && m.GetParameters()[1].ParameterType == typeof(int));
    private static readonly FieldInfo Tint = MethodReader.GetInstructions(NativeModMenu.Sprite).Where(i => i.Opcode == Stfld)
        .Select(i => i.Operand).OfType<FieldInfo>().Single(f => f.FieldType == Color.White.GetType());
    internal static readonly FieldInfo Opacity = MethodReader.GetInstructions(NativeModMenu.Sprite).Where(i => i.Opcode == Stfld)
        .Select(i => i.Operand).OfType<FieldInfo>().Last(f => f.FieldType == typeof(float));
    internal static readonly MethodInfo Draw = NativeModMenu.Dialog.GetMethods(Members).Single(m =>
        m.IsVirtual && m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
        MethodReader.GetInstructions(m).Count(i => i.Operand is MethodInfo c && c.DeclaringType == SpriteManager.Class.Reference) == 1);
    internal static readonly MethodInfo Key = NativeModMenu.Menu.GetMethods(Members).Single(m =>
        !m.IsStatic && m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 &&
        m.GetParameters()[0].ParameterType.FullName == "Microsoft.Xna.Framework.Input.Keys");

    internal static float GetHeight() => (int)Height.Invoke(NativeModMenu.Window.GetValue(null), null);
    internal static float GetX(object point) => (float)X.GetValue(point);
    internal static float GetY(object point) => (float)Y.GetValue(point);
    internal static void SetX(object sprite, float x) => Position.SetValue(sprite,
        NativeModMenu.Point(x, GetY(Position.GetValue(sprite))));
    internal static void Place(object sprite, float x, float y) => Move.Invoke(sprite, [NativeModMenu.Point(x, y), 0]);
    internal static void Slide(object sprite, float y, bool instant)
    {
        // Visual Settings: opening uses 400 ms / easing 1; falling back to the
        // collapsed position uses 600 ms / easing 33 (the native bounce).
        var falling = GetY(Position.GetValue(sprite)) < y;
        MoveY.Invoke(sprite, [y, instant ? 0 : falling ? 600 : 400,
            Enum.ToObject(MoveY.GetParameters()[2].ParameterType, falling ? 33 : 1)]);
    }
    internal static void Size(object sprite, float width, float height) => ScaleVector.SetValue(sprite, NativeModMenu.Point(width * 1.6f, height * 1.6f));
    internal static object Rectangle(object manager, float width, float height, object colour)
    {
        var args = NativeModMenu.Sprite.GetParameters();
        var sprite = NativeModMenu.Sprite.Invoke([WhitePixel.GetValue(null), Enum.ToObject(args[1].ParameterType, 6),
            Enum.Parse(args[2].ParameterType, "TopLeft"), Enum.ToObject(args[3].ParameterType, 0),
            NativeModMenu.Point(0, 0), 0.98f, true, colour, null]);
        Size(sprite, width, height);
        SpriteManager.Add.Invoke(manager, [sprite]);
        return sprite;
    }
    internal static object Heading(object manager, string text, float size, float x, float y, bool blue = false)
    {
        var sprite = NativeModMenu.AddText(manager, text, size, x, y);
        Bold.Invoke(sprite, [true]);
        if (blue) Tint.SetValue(sprite, Colour("LightBlue"));
        return sprite;
    }
    internal static object Colour(string name) => Color.White.GetType().GetProperty(name)!.GetValue(null, null);
    internal static void SetColour(object sprite, string name) => Tint.SetValue(sprite, Colour(name));
    internal static readonly FieldInfo ManagerSprites = MethodReader.GetInstructions(NativeModMenu.Open).Select(i => i.Operand)
        .OfType<FieldInfo>().Distinct().Single(f => f.FieldType == typeof(List<>).MakeGenericType(Position.DeclaringType!));
    internal static object[] Sprites(object manager) => ((IEnumerable)ManagerSprites.GetValue(manager)).Cast<object>().ToArray();
}
