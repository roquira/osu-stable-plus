using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameModes.Options;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Utils.IL;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>The same native dropdown used by stable's global options.</summary>
internal sealed class MirrorDropdown
{
    internal static readonly ConstructorInfo Constructor = MethodReader.GetInstructions(OptionDropdown.Generic.Constructor.Reference)
        .Select(i => i.Operand).OfType<ConstructorInfo>().Single(c => c.GetParameters().Length == 5 &&
            c.GetParameters()[0].ParameterType == SpriteManager.Class.Reference);
    private static readonly Type Type = Constructor.DeclaringType!;
    private static readonly ConstructorInfo FullConstructor = Type.GetConstructors(NativeModMenu.Members).Single(c => c.GetParameters().Length == 7);
    internal static readonly FieldInfo OpensUp = FindDirection();
    private static FieldInfo FindDirection()
    {
        var il = PatchProcessor.GetOriginalInstructions(FullConstructor).ToArray();
        return (FieldInfo)il.Select((instruction, index) => new { instruction, index })
            .Single(x => x.instruction.opcode == System.Reflection.Emit.OpCodes.Stfld && x.index > 0 &&
                il[x.index - 1].IsLdarg(6)).instruction.operand;
    }
    private static readonly MethodInfo Add = Type.GetMethods(DifficultyControl.All).Single(m =>
        m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string), typeof(object) }));
    private static readonly MethodInfo Select = Type.GetMethods(DifficultyControl.All).Single(m => m.ReturnType == typeof(bool) &&
        m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(object), typeof(bool) }));
    private static readonly MethodInfo SelectionCore = MethodReader.GetInstructions(Select).Select(i => i.Operand).OfType<MethodInfo>()
        .Single(m => m.DeclaringType == Type && m.ReturnType == typeof(void));
    private static readonly MethodInfo Close = MethodReader.GetInstructions(SelectionCore).Select(i => i.Operand).OfType<MethodInfo>()
        .First(m => m.DeclaringType == Type && m.ReturnType == typeof(void) && m.GetParameters().Length == 0);
    private static readonly MethodInfo IsOpen = MethodReader.GetInstructions(SelectionCore).Select(i => i.Operand).OfType<MethodInfo>()
        .First(m => m.DeclaringType == Type && m.ReturnType == typeof(bool) && m.GetParameters().Length == 0);
    private static readonly FieldInfo Value = Type.GetFields(DifficultyControl.All).Single(f => f.FieldType == typeof(object));
    private static readonly FieldInfo Changed = MethodReader.GetInstructions(SelectionCore).Select(i => i.Operand)
        .OfType<FieldInfo>().First(f => f.FieldType == typeof(EventHandler));
    private static readonly FieldInfo Sprites = Type.GetFields(DifficultyControl.All).Single(f =>
        f.FieldType == typeof(System.Collections.Generic.List<>).MakeGenericType(NativeModMenu.Sprite.DeclaringType!));
    private readonly object dropdown, title, label, reset;
    private readonly object[] headers;
    private readonly System.Collections.Generic.Dictionary<object, bool> inputs;
    private bool visible;
    internal bool Busy => visible && (bool)IsOpen.Invoke(dropdown, null);
    internal MirrorDropdown(object manager)
    {
        dropdown = FullConstructor.Invoke([manager, "Horizontal", NativeModMenu.Point(96, 0), 94f, 0.995f, false, false]);
        headers = ((IEnumerable)Sprites.GetValue(dropdown)).Cast<object>().ToArray();
        foreach (MirrorAxes axes in Enum.GetValues(typeof(MirrorAxes))) Add.Invoke(dropdown, [axes.ToString(), axes]);
        inputs = ((IEnumerable)Sprites.GetValue(dropdown)).Cast<object>().ToDictionary(s => s, NativeModMenu.IsInteractive);
        Changed.SetValue(dropdown, new EventHandler((sender, _) =>
        {
            if (visible && MirrorSettings.Enabled && sender is MirrorAxes axes) MirrorSettings.Selected = axes;
        }));
        Select.Invoke(dropdown, [MirrorAxes.Horizontal, true]);
        title = NativeModDrawer.Heading(manager, "Mirror", 12, 20, 0);
        label = NativeModMenu.AddText(manager, "Flipped axes:", 13, 20, 0);
        reset = NativeModMenu.AddImage(manager, "setting-reset.png", NativeModMenu.Point(10, 0), 0.85f,
            (_, _) => { if (visible) { MirrorSettings.Selected = MirrorAxes.Horizontal; Select.Invoke(dropdown, [MirrorAxes.Horizontal, true]); } });
    }
    internal void ReadSelection()
    {
        if (visible && MirrorSettings.Enabled && Value.GetValue(dropdown) is MirrorAxes axes) MirrorSettings.Selected = axes;
    }
    internal void SetBelowRate(bool below)
    {
        if ((bool)OpensUp.GetValue(dropdown) == below) return;
        Close.Invoke(dropdown, null);
        OpensUp.SetValue(dropdown, below);
    }
    internal void Refresh(bool show)
    {
        if (visible && !show) Suspend();
        visible = show;
        if (!Equals(Value.GetValue(dropdown), MirrorSettings.Selected)) Select.Invoke(dropdown, [MirrorSettings.Selected, true]);
        NativeModMenu.SetVisible(title, show);
        NativeModMenu.SetVisible(label, show);
        foreach (var sprite in headers) NativeModMenu.SetVisible(sprite, show);
        foreach (var input in inputs) NativeModMenu.SetInteractive(input.Key, show && input.Value);
        var modified = show && MirrorSettings.Selected != MirrorAxes.Horizontal;
        NativeModMenu.SetVisible(reset, modified);
        NativeModMenu.SetInteractive(reset, modified);
    }
    internal void Slide(float y, bool instant)
    {
        Close.Invoke(dropdown, null);
        NativeModDrawer.Slide(title, y, instant);
        NativeModDrawer.Slide(label, y + 23, instant);
        NativeModDrawer.Slide(reset, y + 19, instant);
        for (var i = 0; i < headers.Length; i++) NativeModDrawer.Slide(headers[i], y + 23 + (i == 0 ? 0 : 8), instant);
    }
    internal void Suspend()
    {
        Close.Invoke(dropdown, null);
        foreach (var sprite in ((IEnumerable)Sprites.GetValue(dropdown)).Cast<object>()) NativeModMenu.SetInteractive(sprite, false);
        NativeModMenu.SetInteractive(reset, false);
        visible = false;
    }
}
