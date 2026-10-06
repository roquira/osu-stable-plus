using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Osu.StablePlus.Stubs.GameplayElements.Scoring;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using static Osu.StablePlus.Stubs.Root.Mods;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

internal sealed class ModMenuRateControl : IDisposable
{
    internal static readonly ConditionalWeakTable<object, ModMenuRateControl> Panels = new();
    internal static readonly HashSet<ModMenuRateControl> Active = new();
    private readonly object menu, rateTitle, difficultyTitle, background, edge, modHeading, settingsHeading;
    private readonly object? icon;
    private readonly bool standard;
    private readonly SettingRow rate, halfTimeRate;
    private readonly MirrorDropdown mirror;
    private readonly SettingRow[] difficulty = new SettingRow[4];
    private readonly Dictionary<object, bool> buttons = new();
    private readonly ModDrawerState drawer = new();
    private bool disposed, shown, expanded, buttonsHidden, exiting, rateColumn, mirrorBelowRate;
    private int exitStarted;
    private float width, height, top;
    private string? lastState;

    internal ModMenuRateControl(object menu)
    {
        this.menu = menu;
        CustomRateOptions.ObserveMods(ModManager.ModStatus.Get());
        var manager = NativeModMenu.Manager.GetValue(menu);
        standard = StandardModLayout.IsStandard;
        if (standard)
        {
            StandardModLayout.ArrangeLabels(menu);
            icon = NativeModMenu.AddDifficultyMod(menu);
            NativeModMenu.AddMod(menu, MirrorSettings.Flag, 1);
        }
        background = NativeModDrawer.Rectangle(manager, 1, 1, NativeModDrawer.Colour("Black"));
        edge = NativeModDrawer.Rectangle(manager, 1, 2, NativeModDrawer.Colour("DodgerBlue"));
        NativeModMenu.OnClick(background, (_, _) => { });
        modHeading = NativeModDrawer.Heading(manager, "Mod", 22, 4, 0, true);
        settingsHeading = NativeModDrawer.Heading(manager, "Settings", 20, 40, 0);
        foreach (var heading in new[] { modHeading, settingsHeading })
            NativeModMenu.OnClick(heading, (_, _) => { if (shown) { drawer.TogglePin(); ApplyDrawer(); } });
        rateTitle = NativeModDrawer.Heading(manager, "Double Time", 12, 20, 0);
        rate = new SettingRow(manager, 1.01, 2, 0.01, speed =>
        {
            if (CustomRateOptions.IsEnabled(ModManager.ModStatus.Get())) CustomRateOptions.Set(speed, CustomRateOptions.Selected.AdjustPitch);
        }, () => CustomRateOptions.Set(1.5, CustomRateOptions.Selected.AdjustPitch));
        halfTimeRate = new SettingRow(manager, 0.5, 0.99, 0.01, speed =>
        {
            if ((ModManager.ModStatus.Get() & HalfTime) != 0)
                CustomRateOptions.SetHalfTime(speed, CustomRateOptions.SelectedHalfTime.AdjustPitch);
        }, () => CustomRateOptions.SetHalfTime(0.75, CustomRateOptions.SelectedHalfTime.AdjustPitch));
        difficultyTitle = NativeModDrawer.Heading(manager, "Difficulty Adjust", 12, 220, 0);
        mirror = new MirrorDropdown(manager);
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            difficulty[i] = new SettingRow(manager, 0, 10, 0.1,
                value => { if (DifficultyControl.Enabled) DifficultyControl.Set(index, value); },
                () => DifficultyControl.Set(index, null));
        }
        Active.Add(this);
        Refresh(true);
    }

    internal void RegisterButtons(IEnumerable<object> sprites)
    {
        foreach (var sprite in sprites) buttons[sprite] = NativeModMenu.IsInteractive(sprite);
    }

    internal void Open()
    {
        drawer.Reset();
        exiting = false;
        expanded = false;
        SetButtonsHidden(false, true);
        Refresh(true);
    }

    internal void Tick()
    {
        if (disposed || !(bool)NativeModMenu.IsOpen.GetValue(menu)) return;
        StableScoreMenu.Refresh(menu);
        mirror.ReadSelection();
        Refresh();
        if (exiting)
        {
            var exitTop = NativeModDrawer.GetY(NativeModDrawer.Position.GetValue(edge));
            if (buttonsHidden)
            {
                foreach (var button in buttons)
                    NativeModDrawer.Opacity.SetValue(button.Key, ModDrawerState.ButtonOpacity(height - 45, top, exitTop));
                if (exitTop >= height - 45) SetButtonsHidden(false);
            }
            if (unchecked(Environment.TickCount - exitStarted) >= 600)
            {
                exiting = false;
                Refresh(true);
            }
            return;
        }
        if (!shown) return;
        var pointer = NativeModDrawer.Pointer.Invoke(null, null);
        var x = NativeModDrawer.GetX(pointer);
        var y = NativeModDrawer.GetY(pointer);
        // Visual Settings uses the destination bounds for hover detection,
        // allowing the cursor to enter the controls while the drawer rises.
        var currentTop = NativeModDrawer.GetY(NativeModDrawer.Position.GetValue(edge));
        drawer.Update(x >= 0 && x <= width && y >= (expanded ? top : height - 45) && y <= height,
            rate.Dragging || halfTimeRate.Dragging || difficulty.Any(row => row.Dragging) || mirror.Busy);
        ApplyDrawer();
        if (buttonsHidden)
        {
            var opacity = ModDrawerState.ButtonOpacity(height - 45, top, currentTop);
            foreach (var button in buttons) NativeModDrawer.Opacity.SetValue(button.Key, opacity);
        }
        // Keep covered buttons disabled until the closing animation finishes.
        if (!expanded && buttonsHidden && Math.Abs(currentTop - (height - 45)) < 0.5)
            SetButtonsHidden(false);
    }

    internal bool AdjustHoveredSlider(object key)
    {
        if (disposed || !shown || exiting || !(bool)NativeModMenu.IsOpen.GetValue(menu)) return false;
        if (rate.AdjustIfHovered(key) || halfTimeRate.AdjustIfHovered(key)) return true;
        foreach (var row in difficulty) if (row.AdjustIfHovered(key)) return true;
        return false;
    }

    private void SetButtonsHidden(bool hide, bool force = false)
    {
        if (!force && buttonsHidden == hide) return;
        buttonsHidden = hide;
        foreach (var button in buttons)
        {
            NativeModMenu.SetInteractive(button.Key, !hide && button.Value);
            // Clear the dialog's initial fade once, then use drawer-position
            // opacity every frame instead of abruptly hiding the sprites.
            NativeModMenu.SetVisible(button.Key, true);
        }
    }

    private void ApplyDrawer(bool instant = false, bool force = false)
    {
        if (!instant && !force && expanded == drawer.Expanded) return;
        expanded = drawer.Expanded;
        var target = expanded ? top : height - 45;
        SlideTo(target, instant);
        if (expanded) SetButtonsHidden(true);
        else if (instant) SetButtonsHidden(false);
    }

    private void SlideTo(float target, bool instant)
    {
        NativeModDrawer.Place(background, 0, NativeModDrawer.GetY(NativeModDrawer.Position.GetValue(background)));
        NativeModDrawer.Slide(background, target + 2, instant);
        NativeModDrawer.Slide(edge, target, instant);
        NativeModDrawer.Slide(modHeading, target + 3, instant);
        NativeModDrawer.Slide(settingsHeading, target + 18, instant);
        NativeModDrawer.Slide(rateTitle, target + 47, instant);
        NativeModDrawer.Slide(difficultyTitle, target + 47, instant);
        rate.Slide(target + 70, instant);
        halfTimeRate.Slide(target + 70, instant);
        mirror.Slide(target + (mirrorBelowRate ? 107 : 47), instant);
        for (var i = 0; i < 4; i++) difficulty[i].Slide(target + 70 + (i / 2) * 46, instant);
    }

    private void Layout(bool hasRate)
    {
        width = NativeModMenu.GetWidth();
        height = NativeModDrawer.GetHeight();
        top = standard ? Math.Max(height - 170, NativeModDrawer.GetY(NativeModMenu.ModPosition.Invoke(menu, [3, 1])) + 28)
            : height - 110;
        NativeModDrawer.Size(background, width, height - top);
        NativeModDrawer.Size(edge, width, 2);
        NativeModDrawer.Place(edge, 0, height - 45);
        NativeModDrawer.Place(modHeading, 4, height - 42);
        NativeModDrawer.Place(settingsHeading, 40, height - 27);
        // Keep native widget sizes at all aspect ratios; spread the columns
        // across the available width instead of scaling the entire mod menu.
        var column = (width - 40) / (hasRate ? 3 : 2);
        var daX = hasRate ? 20 + column : 20;
        NativeModDrawer.Place(rateTitle, 20, height + 2);
        NativeModDrawer.Place(difficultyTitle, daX, height + 2);
        rate.Position(20, height + 25);
        halfTimeRate.Position(20, height + 25);
        for (var i = 0; i < 4; i++) difficulty[i].Position(daX + (i % 2) * column, height + 25 + (i / 2) * 46);
        rateColumn = hasRate;
        ApplyDrawer(true);
    }

    private void LayoutColumns(bool hasRate)
    {
        // A mod toggle changes columns, not the drawer's vertical position or
        // its active native movement transforms. In particular, don't call
        // Place/ApplyDrawer(true) during a closing bounce.
        var column = (width - 40) / (hasRate ? 3 : 2);
        var daX = hasRate ? 20 + column : 20;
        NativeModDrawer.SetX(difficultyTitle, daX);
        for (var i = 0; i < 4; i++) difficulty[i].PositionX(daX + (i % 2) * column);
        rateColumn = hasRate;
    }

    internal void Refresh(bool forceVisibility = false)
    {
        if (disposed) return;
        NativeModMenu.RestoreKeyboardFocus(menu);
        var mods = ModManager.ModStatus.Get();
        CustomRateOptions.ObserveMods(mods);
        var enabled = CustomRateOptions.IsEnabled(mods) && ModeRateTiming.Supports(StandardModLayout.ModeFor(DifficultyControl.CurrentBeatmap.Invoke(null, null)));
        var da = standard && DifficultyControl.Enabled;
        var mr = standard && MirrorSettings.Enabled;
        if (!mr) MirrorSettings.Selected = MirrorAxes.Horizontal;
        if (!da) DifficultyControl.Selected = new DifficultySettings(null, null, null, null);
        var map = DifficultyControl.CurrentBeatmap.Invoke(null, null);
        var speed = CustomRateOptions.SelectedFor(mods).Speed;
        var state = mods + ":" + da + ":" + speed + ":" + MirrorSettings.Selected + ":" + NativeModMenu.GetWidth() + ":" + NativeModDrawer.GetHeight();
        for (var i = 0; i < 4; i++) state += ":" + DifficultyControl.Value(map, i) + ":" + DifficultyControl.Default(map, i);
        if (!forceVisibility && state == lastState) return;
        var layoutChanged = forceVisibility || width != NativeModMenu.GetWidth() || height != NativeModDrawer.GetHeight();
        lastState = state;
        if (icon != null) NativeModMenu.SyncDifficultyMod(icon, forceVisibility);
        if (!enabled && !da && !mr && !forceVisibility && (shown || exiting) && (bool)NativeModMenu.IsOpen.GetValue(menu))
        {
            if (!exiting)
            {
                shown = expanded = false;
                exiting = true;
                exitStarted = Environment.TickCount;
                drawer.Reset();
                // Keep the current labels and values visible during departure,
                // but release any drag and disable all outgoing controls now.
                rate.Suspend();
                halfTimeRate.Suspend();
                mirror.Suspend();
                foreach (var row in difficulty) row.Suspend();
                foreach (var sprite in new[] { background, edge, modHeading, settingsHeading })
                    NativeModMenu.SetInteractive(sprite, false);
                SlideTo(height, false);
            }
            return;
        }
        var returning = exiting && (enabled || da || mr);
        var returnTop = returning ? NativeModDrawer.GetY(NativeModDrawer.Position.GetValue(edge)) : height;
        exiting = false;
        var appearing = (enabled || da || mr) && (!shown || forceVisibility) && (bool)NativeModMenu.IsOpen.GetValue(menu);
        shown = enabled || da || mr;
        if (!shown) { drawer.Reset(); SetButtonsHidden(false); }
        var mirrorRowChanged = mirrorBelowRate != enabled;
        mirrorBelowRate = enabled;
        mirror.SetBelowRate(mirrorBelowRate);
        if (layoutChanged) Layout(enabled || mr);
        else if (rateColumn != (enabled || mr)) LayoutColumns(enabled || mr);
        // Only reposition Mirror when the speed section appears/disappears;
        // leave the drawer and other controls' ongoing animations intact.
        if (mirrorRowChanged && !layoutChanged && !appearing)
            mirror.Slide((expanded ? top : height - 45) + (mirrorBelowRate ? 107 : 47), false);
        foreach (var sprite in new[] { background, edge, modHeading, settingsHeading })
        {
            NativeModMenu.SetVisible(sprite, shown);
            NativeModMenu.SetInteractive(sprite, shown && sprite != edge);
        }
        pText.SetText.Invoke(rateTitle, [(mods & Nightcore) != 0 ? "Nightcore" : (mods & HalfTime) != 0 ? "Half Time" : "Double Time"]);
        NativeModMenu.SetVisible(rateTitle, enabled);
        NativeModMenu.SetVisible(difficultyTitle, da);
        mirror.Refresh(mr);
        if (icon != null) NativeModMenu.SyncDifficultyMod(icon, forceVisibility);
        var halfTime = (mods & HalfTime) != 0;
        var rateLabel = "Rate (" + speed.ToString("0.##", CultureInfo.InvariantCulture) + "x)";
        rate.Refresh(rateLabel, CustomRateOptions.Selected.Speed, enabled && !halfTime, speed != 1.5, forceVisibility);
        halfTimeRate.Refresh(rateLabel, CustomRateOptions.SelectedHalfTime.Speed, enabled && halfTime, speed != 0.75, forceVisibility);
        for (var i = 0; i < 4; i++)
        {
            var value = DifficultyControl.Value(map, i);
            difficulty[i].Refresh(DifficultySettings.Label(i, value, enabled ? speed : 1), value,
                da, Math.Abs(value - DifficultyControl.Default(map, i)) > 0.00001, forceVisibility);
        }
        if (appearing)
        {
            // Enter from below the viewport, using the same native rise as
            // expansion. Start after updating slider values so their knob
            // positioning does not interrupt the entrance animation.
            SlideTo(returning ? returnTop : height, true);
            ApplyDrawer(force: true);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Active.Remove(this);
        rate.Dispose();
        halfTimeRate.Dispose();
        mirror.Suspend();
        foreach (var row in difficulty) row.Dispose();
        CustomRateOptions.Persist();
    }

    private sealed class SettingRow : IDisposable
    {
        private readonly object slider, label, reset;
        private readonly object[] sprites;
        private readonly bool[] input;
        private bool visible;
        private bool suspended;
        internal bool Dragging => visible && (bool)NativeDrawerSlider.Dragging.Invoke(slider, null);
        internal bool AdjustIfHovered(object key)
        {
            if (!visible || !(bool)NativeDrawerSlider.Hovered.GetValue(slider)) return false;
            // Route through the native slider implementation, including its
            // bounds, step, feedback sound and finished-change callback.
            NativeModMenu.SliderKeyboard.SetValue(slider, true);
            try { return (bool)NativeModMenu.SliderKey.Invoke(slider, [null, key, false]); }
            finally { NativeModMenu.SliderKeyboard.SetValue(slider, false); }
        }
        internal SettingRow(object manager, double min, double max, double step, Action<double> change, Action restore)
        {
            label = NativeModMenu.AddText(manager, "", 13, 0, 0);
            slider = NativeModMenu.Slider.Invoke([manager, min, max, min, NativeModMenu.Point(0, 22), 170]);
            sprites = NativeModMenu.GetSprites(slider).ToArray();
            input = sprites.Select(NativeModMenu.IsInteractive).ToArray();
            NativeModMenu.SliderStep.SetValue(slider, step);
            NativeModMenu.Bind(NativeModMenu.SliderChanged, slider, new Action<bool>(finished =>
            {
                if (!visible) return;
                change(Math.Round((double)NativeModMenu.SliderValue.GetValue(slider) / step, MidpointRounding.AwayFromZero) * step);
                if (finished) CustomRateOptions.Persist();
            }));
            reset = NativeModMenu.AddImage(manager, "setting-reset.png", NativeModMenu.Point(-10, 0), 1,
                (_, _) => { if (visible) { restore(); CustomRateOptions.Persist(); } });
        }
        internal void Position(float x, float y)
        {
            var previous = NativeModDrawer.Position.GetValue(label);
            var dx = x - NativeModDrawer.GetX(previous);
            NativeModDrawer.Place(label, x, y);
            NativeModDrawer.Place(reset, x - 10, y);
            foreach (var sprite in sprites)
                NativeModDrawer.Place(sprite, NativeModDrawer.GetX(NativeModDrawer.Position.GetValue(sprite)) + dx, y + 22);
        }
        internal void Slide(float y, bool instant)
        {
            NativeModDrawer.Slide(label, y, instant);
            NativeModDrawer.Slide(reset, y, instant);
            foreach (var sprite in sprites) NativeModDrawer.Slide(sprite, y + 22, instant);
        }
        internal void PositionX(float x)
        {
            var dx = x - NativeModDrawer.GetX(NativeModDrawer.Position.GetValue(label));
            NativeModDrawer.SetX(label, x);
            NativeModDrawer.SetX(reset, x - 10);
            foreach (var sprite in sprites)
                NativeModDrawer.SetX(sprite, NativeModDrawer.GetX(NativeModDrawer.Position.GetValue(sprite)) + dx);
        }
        internal void Suspend()
        {
            visible = false;
            // Keep the visuals for the drawer's exit animation, but remember
            // that they still need reconciling if another mod reopens it.
            suspended = true;
            // Native slider Dispose only releases its input subscriptions;
            // the same widget can be shown again if a mod is re-enabled.
            ((IDisposable)slider).Dispose();
            foreach (var sprite in sprites) NativeModMenu.SetInteractive(sprite, false);
            NativeModMenu.SetInteractive(reset, false);
        }
        internal void Refresh(string text, double value, bool show, bool modified, bool force)
        {
            pText.SetText.Invoke(label, [text]);
            // Setting even an unchanged value repositions the native knob,
            // cancelling its movement animation on an unrelated mod toggle.
            if ((double)NativeModMenu.SliderValue.GetValue(slider) != value)
                NativeModMenu.SliderSet.Invoke(slider, [value, true, false]);
            if (force || suspended || visible != show)
            {
                visible = show;
                suspended = false;
                NativeModMenu.SliderKeyboard.SetValue(slider, false);
                NativeModMenu.SliderVisible.Invoke(slider, [show]);
                for (var i = 0; i < sprites.Length; i++) NativeModMenu.SetInteractive(sprites[i], show && input[i]);
                // Native visibility is cached separately and uses a fade.
                // Disabled rows must be hidden before the drawer returns.
                if (!show) foreach (var sprite in sprites) NativeModMenu.SetVisible(sprite, false);
                NativeModMenu.SetVisible(label, show);
            }
            NativeModMenu.SetInteractive(reset, show && modified);
            NativeModMenu.SetVisible(reset, show && modified);
        }
        public void Dispose() => ((IDisposable)slider).Dispose();
    }
}

internal static class NativeDrawerSlider
{
    private static readonly MethodInfo HoverEnter = Osu.StablePlus.Utils.IL.MethodReader.GetInstructions(NativeModMenu.Slider)
        .Where(i => i.Opcode == System.Reflection.Emit.OpCodes.Ldftn).Select(i => i.Operand).OfType<MethodInfo>()
        .First(m => m.DeclaringType == NativeModMenu.Slider.DeclaringType && m.GetParameters().Length == 2 &&
            m.GetParameters()[0].ParameterType == typeof(object) && m.GetParameters()[1].ParameterType == typeof(EventArgs));
    private static readonly MethodInfo SetHovered = Osu.StablePlus.Utils.IL.MethodReader.GetInstructions(HoverEnter)
        .Select(i => i.Operand).OfType<MethodInfo>().First(m => m.DeclaringType == NativeModMenu.Slider.DeclaringType &&
            m.ReturnType == typeof(void) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool));
    internal static readonly FieldInfo Hovered = Osu.StablePlus.Utils.IL.MethodReader.GetInstructions(SetHovered)
        .Where(i => i.Opcode == System.Reflection.Emit.OpCodes.Stfld).Select(i => i.Operand).OfType<FieldInfo>().Single();
    internal static readonly MethodInfo Dragging = Osu.StablePlus.Utils.IL.MethodReader.GetInstructions(NativeModMenu.SliderKey)
        .Select(i => i.Operand).OfType<MethodInfo>().First(m => m.DeclaringType == NativeModMenu.Slider.DeclaringType && m.ReturnType == typeof(bool));
}

[OsuPatch, HarmonyPatch]
internal static class UpdateModDrawer
{
    [HarmonyTargetMethod] private static MethodBase Target() => NativeModDrawer.Draw;
    [HarmonyPrefix]
    private static void Before(object __instance)
    {
        if (ModMenuRateControl.Panels.TryGetValue(__instance, out var panel)) panel.Tick();
    }
}

[OsuPatch, HarmonyPatch]
internal static class ModDrawerKeyboard
{
    [HarmonyTargetMethod] private static MethodBase Target() => NativeModDrawer.Key;
    [HarmonyPrefix]
    private static bool Before(object __instance, object __0, ref bool __result)
    {
        var key = __0.ToString();
        if ((key != "Left" && key != "Right") || !ModMenuRateControl.Panels.TryGetValue(__instance, out var panel) ||
            !panel.AdjustHoveredSlider(__0)) return true;
        __result = true;
        return false;
    }
}

[OsuPatch, HarmonyPatch]
internal static class RestoreModMenuRateVisibility
{
    [HarmonyTargetMethod] private static MethodBase Target() => NativeModMenu.Open;
    [HarmonyPostfix]
    private static void After(object __instance)
    {
        if (ModMenuRateControl.Panels.TryGetValue(__instance, out var panel)) panel.Open();
    }
}

[OsuPatch, HarmonyPatch]
internal static class AddModMenuRateControl
{
    [HarmonyTargetMethod] private static MethodBase Target() => NativeModMenu.AddButton;
    [HarmonyPrefix]
    private static void Before(object __instance, out object[]? __state)
    {
        __state = null;
        if (__instance.GetType() == NativeModMenu.Menu) StandardModLayout.ObserveSelection();
        if (__instance.GetType() != NativeModMenu.Menu) return;
        if (!ModMenuRateControl.Panels.TryGetValue(__instance, out _))
            ModMenuRateControl.Panels.Add(__instance, new ModMenuRateControl(__instance));
        __state = NativeModDrawer.Sprites(NativeModMenu.Manager.GetValue(__instance));
    }
    [HarmonyPostfix]
    private static void After(object __instance, object[]? __state)
    {
        if (__state != null && ModMenuRateControl.Panels.TryGetValue(__instance, out var panel))
            panel.RegisterButtons(NativeModDrawer.Sprites(NativeModMenu.Manager.GetValue(__instance)).Except(__state));
    }
}

[OsuPatch, HarmonyPatch]
internal static class DisposeModMenuRateControl
{
    [HarmonyTargetMethod] private static MethodBase Target() => NativeModMenu.Dispose;
    [HarmonyPrefix]
    private static void Before(object __instance)
    {
        if (!ModMenuRateControl.Panels.TryGetValue(__instance, out var panel)) return;
        ModMenuRateControl.Panels.Remove(__instance);
        panel.Dispose();
    }
}
