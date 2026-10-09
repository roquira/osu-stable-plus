using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Osu.StablePlus.Hook.Patches.Mods.CustomRate;
using Osu.StablePlus.Stubs.Graphics.Sprites;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Utils;
using Osu.StablePlus.Utils.IL;
using Scheduler = Osu.StablePlus.Stubs.Helpers.Scheduler;

namespace Osu.StablePlus.Hook;

/// <summary>Install on the game thread, yielding to stable between steps in its empty Busy scene.</summary>
internal sealed class PatchLoadingScreen
{
    internal static readonly MethodInfo Delay = Scheduler.Class.Reference.GetMethods(NativeModMenu.Members)
        .Single(m => m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[]
        { Scheduler.Add.Reference.GetParameters()[0].ParameterType, typeof(int), typeof(bool) }));
    internal static readonly MethodInfo Key = NativeModMenu.Dialog.GetMethods(NativeModMenu.Members)
        .Single(m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType.FullName == "Microsoft.Xna.Framework.Input.Keys");
    internal static readonly ConstructorInfo Constructor = NativeModMenu.Dialog.GetConstructors(NativeModMenu.Members)
        .Single(c => c.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string), typeof(bool) }));

    // Busy has no scene Initialize() to call GameBase.FadeIn for us.
    internal static readonly MethodInfo FadeIn = GameBase.Class.Reference
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        .Single(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0 &&
            MethodReader.GetInstructions(m).Any(i => Equals(i.Operand, 140d)));
    internal static readonly FieldInfo TransitionOpacity = MethodReader.GetInstructions(FadeIn)
        .Select(i => i.Operand).OfType<FieldInfo>().Distinct().Single(f => f.FieldType == typeof(double));

    // The rotating loader used by stable's beatmap reprocessing screen.
    internal static readonly MethodInfo Spinner = OsuAssembly.Types
        .Where(t => typeof(IDisposable).IsAssignableFrom(t) &&
            t.GetFields(NativeModMenu.Members).Count(f => f.FieldType == SpriteManager.Class.Reference) == 2 &&
            t.GetFields(NativeModMenu.Members).Any(f => f.FieldType == pText.SetText.Reference.DeclaringType))
        .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .Single(m => m.ReturnType == NativeModMenu.Sprite.DeclaringType &&
            m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { Color.White.GetType() }));

    private static PatchLoadingScreen? active;
    private readonly Harmony guard = new("osu-stable-plus.loading");
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly IEnumerator<string> steps;
    private readonly Action<Exception?> finished;
    private readonly string returnMode;
    private object? dialog, title, bar;
    private bool enteredBusy;
    private bool closed;
    private bool revealed;
    private int completed;
    private readonly int total;

    internal PatchLoadingScreen(IEnumerable<string> steps, int total, Action<Exception?> finished)
    {
        this.steps = steps.GetEnumerator();
        this.total = total;
        this.finished = finished;
        var previous = GameBase.Mode.Reference.GetValue(null).ToString();
        returnMode = previous is "Play" or "SelectPlay" or "Rank" ? "SelectPlay" : "Menu";
    }

    internal void Start()
    {
        try
        {
            // Do not leave the editor (and its unsaved work), updates or multiplayer implicitly.
            var mode = GameBase.Mode.Reference.GetValue(null).ToString();
            if (mode is not ("Menu" or "SelectPlay" or "Play" or "Rank"))
                throw new InvalidOperationException($"Return to the main menu or song selection before loading {Product.Name}.");
            active = this;
            guard.Patch(Key, prefix: new HarmonyMethod(typeof(PatchLoadingScreen), nameof(BlockKey)));
            // Busy already blocks options natively. Avoid a temporary wrapper on
            // CanExpand: removing it after its gameplay transpiler is installed
            // triggers invalid IL generation in Harmony on .NET Framework.
            ChangeMode("Busy");
            Post(Tick);
        }
        catch (Exception e) { Finish(e); }
    }

    // Steps run back to back until this much time has passed, then yield a frame.
    private const int FrameBudget = 33;

    // A zero delay runs on the scheduler's next update, after stable has drawn a frame.
    private static void Post(Action action, int delay = 0) => Delay.Invoke(GameBase.Scheduler.Get(),
        [VoidDelegate.MakeInstance(action), delay, false]);

    private void Tick()
    {
        if (closed) return;
        try
        {
            if (GameBase.Mode.Reference.GetValue(null).ToString() != "Busy")
            {
                if (enteredBusy || elapsed.ElapsedMilliseconds > 10000)
                    throw new InvalidOperationException("Could not keep osu! in its loading screen. Patch installation stopped.");
                Post(Tick);
                return;
            }
            if (!enteredBusy)
            {
                enteredBusy = true;
                dialog = Constructor.Invoke(["", false]);
                var manager = NativeModMenu.Manager.GetValue(dialog);
                var x = (NativeModMenu.GetWidth() - 300) / 2;
                var y = NativeModDrawer.GetHeight() / 2 - 35;
                title = NativeModMenu.AddText(manager, $"Loading {Product.Name}...", 24, x, y);
                var track = NativeModDrawer.Rectangle(manager, 300, 3, NativeModDrawer.Colour("DimGray"));
                NativeModDrawer.Place(track, x, y + 42);
                bar = NativeModDrawer.Rectangle(manager, 1, 3, NativeModDrawer.Colour("HotPink"));
                NativeModDrawer.Place(bar, x, y + 42);
                var spinner = Spinner.Invoke(null, [NativeModDrawer.Colour("HotPink")]);
                NativeModDrawer.Position.SetValue(spinner, NativeModMenu.Point(0, 70));
                SpriteManager.Add.Invoke(manager, [spinner]);
                NativeModMenu.FocusedDialog.SetValue(null, dialog);
                NativeModMenu.Open.Invoke(dialog, null);
                FadeIn.Invoke(null, null);
                elapsed.Restart();
                Console.WriteLine("[Loading] Opened native dialog; waiting for the transition to reveal it.");
                Post(Tick, 400);
                return;
            }
            if (!revealed)
            {
                // Do not start expensive binding scans while stable is still
                // drawing its black transition over the loading dialog.
                if ((double)TransitionOpacity.GetValue(null) > 0)
                {
                    if (elapsed.ElapsedMilliseconds > 10000)
                        throw new InvalidOperationException("The loading-screen transition did not finish.");
                    Post(Tick);
                    return;
                }
                revealed = true;
                Console.WriteLine("[Loading] Transition finished; starting patch installation.");
                Post(Tick);
                return;
            }
            // Each callback performs steps for up to a frame budget, then leaves stable
            // a frame for native rendering/input processing before the next batch.
            var frame = Stopwatch.StartNew();
            do
            {
                if (!steps.MoveNext()) { Finish(null); return; }
                completed++;
                Console.WriteLine("[Initialize] " + steps.Current);
            } while (frame.ElapsedMilliseconds < FrameBudget);
            pText.SetText.Invoke(title, [$"Loading {Product.Name}... {Math.Min(100, completed * 100 / total)}%"]);
            NativeModDrawer.Size(bar!, Math.Max(1, 300f * completed / total), 3);
            Post(Tick);
        }
        catch (Exception e) { Finish(e); }
    }

    private void Finish(Exception? error)
    {
        if (closed) return;
        closed = true;
        try
        {
            steps.Dispose();
            if (dialog != null)
            {
                if (ReferenceEquals(NativeModMenu.FocusedDialog.GetValue(null), dialog))
                    NativeModMenu.FocusedDialog.SetValue(null, null);
                NativeModMenu.Dispose.Invoke(dialog, [true]);
                GC.SuppressFinalize(dialog);
            }
        }
        catch (Exception e) { Console.WriteLine(e); error ??= e; }
        finally
        {
            active = null;
            // Keep the scoped key prefix installed. Rebuilding stable's
            // obfuscated original during Unpatch can produce invalid IL even
            // after every gameplay patch has installed successfully. With no
            // active dialog the prefix simply passes through to native input.
            try
            {
                if (GameBase.Mode.Reference.GetValue(null).ToString() == "Busy")
                    ChangeMode(returnMode);
            }
            catch (Exception e) { Console.WriteLine(e); error ??= e; }
            finished(error);
        }
    }

    private static void ChangeMode(string mode) => RulesetCompatibility.ExitPlayer.Invoke(null,
        [Enum.Parse(GameBase.Mode.Reference.FieldType, mode), true]);
    internal static bool BlockKey(object __instance, ref bool __result)
    {
        if (active == null || !ReferenceEquals(__instance, active.dialog)) return true;
        __result = true;
        return false;
    }
}
