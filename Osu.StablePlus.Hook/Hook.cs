using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using HarmonyLib;
using JetBrains.Annotations;
using Osu.StablePlus.Hook.Patches;

using Osu.StablePlus.Stubs.Wrappers;
using Osu.StablePlus.Utils;

namespace Osu.StablePlus.Hook;

[UsedImplicitly]
public static class Hook
{
    private static Harmony _harmony = null!;
    private static int initializationStarted;
    // Owns the settings subscriptions for the game's lifetime; tests dispose their own instances.
    private static SettingsPersistence? settingsPersistence;

    /// <summary>
    ///     An instance of all patch options that have been initialized.
    /// </summary>
    public static IReadOnlyList<PatchOptions> PatchOptions { get; private set; } = null!;

    /// <summary>
    ///     Entry point into the hook called by the injector.
    /// </summary>
    [UsedImplicitly]
    public static int Initialize(string _)
    {
        if (System.Threading.Interlocked.Exchange(ref initializationStarted, 1) != 0) return 0;
        if (!InjectionGuard.TryAcquire())
        {
            try { Notifications.ShowMessage($"Another osu! patcher build is already loaded. Restart osu! before loading {Product.Name}.", NotificationColor.Warning, 10000); }
            catch { }
            System.Threading.Interlocked.Exchange(ref initializationStarted, 0);
            return 0;
        }
        var osuDir = Path.GetDirectoryName(OsuAssembly.Assembly.Location)!;

#if DEBUG
        ConsoleHook.InitializeConsoleOutput();
        DebugHook.Initialize();

#else
        ConsoleHook.InitializeLogOutput(osuDir);
#endif

        Console.WriteLine($"[Initialize] Hook: {typeof(Hook).Assembly.Location}; build: {typeof(Hook).Module.ModuleVersionId}");
        try
        {
            PatchingRuntime.Configure();
            // This gate is mandatory: never continue through the optional patch loop
            // if the offline session cannot be established.
            OfflineSession.Initialize(Environment.GetCommandLineArgs());
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            try { Notifications.ShowMessage(e.Message, NotificationColor.Error, 20000); }
            catch { }
            System.Threading.Interlocked.Exchange(ref initializationStarted, 0);
            InjectionGuard.Release();
            return 0;
        }

        // Patch only between game updates. Installing hooks from the injector thread
        // races gameplay and type initializers used by the newly installed hooks.
        Console.WriteLine("[Initialize] Scheduling patch installation on the game thread.");
        var callback = VoidDelegate.MakeInstance(() => InitializeOnGameThread(osuDir));
        Osu.StablePlus.Stubs.Helpers.Scheduler.Add.Invoke(Osu.StablePlus.Stubs.Root.GameBase.Scheduler.Get(), [callback, true]);
        return 0;
    }

    private static void InitializeOnGameThread(string osuDir)
    {
        try
        {
            var types = AccessTools.GetTypesFromAssembly(typeof(Hook).Assembly)
                .Where(OsuPatchProcessor.IsOsuPatch).ToArray();
            var failures = 0;
            new PatchLoadingScreen(InstallSteps(osuDir, types, () => failures++), types.Length + 4, error =>
            {
                if (error != null)
                {
                    Console.WriteLine(error);
                    ShowErrorNotification();
                    return;
                }
                Console.WriteLine($"[Initialize] Complete; failed patches: {failures}.");
                Notifications.ShowMessage(
                    failures != 0 ? $"{Product.Name} loaded with {failures} failed patches. See {Product.LogFilename}; some features are unavailable." :
                    OfflineSession.RequiresOfflineMode(Environment.GetCommandLineArgs())
                        ? $"{Product.Name} initialized in offline mode. Bancho login is disabled until restart."
                        : $"{Product.Name} initialized!",
                    failures == 0 ? NotificationColor.Neutral : NotificationColor.Warning, 5000);
            }).Start();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            ShowErrorNotification();
        }
    }

    private static IEnumerable<string> InstallSteps(string osuDir, Type[] types, Action failed)
    {
        var settings = Settings.ReadFromDisk(osuDir);
        PatchOptions = Patches.PatchOptions.CreateAllPatchOptions().ToList();
        PatchOptions.Do(options => options.Load(settings));
        settingsPersistence = new SettingsPersistence(PatchOptions, osuDir,
            Patches.LivePerformance.PerformanceDisplay.Schedule);
        _harmony = new Harmony("osu-stable-plus");
        yield return "Loaded settings.";
        foreach (var type in types)
        {
            Console.WriteLine($"[Initialize] Installing {type.Name}.");
            try { new OsuPatchProcessor(_harmony, type).Patch(); }
            catch (Exception e)
            {
                failed();
                Console.WriteLine($"Failed to initialize patch {type.Name}: {e}");
                if (PatchingRuntime.IsNativeFailure(e))
                    throw new InvalidOperationException("Native patch compilation failed. Installation stopped; restart osu! before trying again.", e);
            }
            yield return $"Processed {type.Name}.";
        }
        Patches.Mods.CustomRate.NativeStableScoring.VerifyInstallation();
        Patches.Mods.CustomRate.ModeRateTiming.VerifyInstallation();
        yield return "Verified scoring hooks.";
        OptionsUtils.ReloadOptions();
        yield return "Reloaded options.";
    }

    /// <summary>
    ///     Show a generic error notification to the user.
    /// </summary>
    private static void ShowErrorNotification()
    {
        try
        {
            Notifications.ShowMessage(
                $"{Product.Name} experienced an error! See {Product.LogFilename} for details.",
                NotificationColor.Error,
                20000);
        }
        catch (Exception e2)
        {
            Console.WriteLine("Failed to show error notification!");
            Console.WriteLine(e2);
        }
    }
}
