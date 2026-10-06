using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Osu.StablePlus.Stubs.Helpers;
using Osu.StablePlus.Stubs.Online;
using Osu.StablePlus.Stubs.Root;
using Osu.StablePlus.Stubs.Wrappers;

namespace Osu.StablePlus.Hook;

// Mandatory bootstrap protection, deliberately outside the optional patch loop.
internal static class OfflineSession
{
    internal const string LoginBlockedMessage = "Bancho login is disabled while " + Product.Name + " is loaded. Restart osu! to log in.";
    private static readonly object Sync = new();
    private static bool installed;

    internal static bool RequiresOfflineMode(string[] arguments)
    {
        var switches = arguments.Select((value, index) => new { value, index })
            .Where(a => string.Equals(a.value, "-devserver", StringComparison.Ordinal)).ToArray();
        if (switches.Length != 1 || switches[0].index + 1 >= arguments.Length) return true;
        var domain = arguments[switches[0].index + 1].Trim().TrimEnd('.');
        // Only an explicit, valid non-official domain retains private-server behaviour.
        if (Uri.CheckHostName(domain) == UriHostNameType.Unknown) return true;
        return domain.Equals("ppy.sh", StringComparison.OrdinalIgnoreCase) ||
               domain.EndsWith(".ppy.sh", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Initialize(string[] arguments) => Initialize(arguments, RunOnGameThread);

    internal static bool Initialize(string[] arguments, Action<Action> runOnGameThread)
    {
        if (!RequiresOfflineMode(arguments)) return false;
        lock (Sync)
        {
            if (installed) return true;
            // Resolve every required hook before changing anything. A changed stable build
            // must not silently continue with only some of the login protection installed.
            var login = BanchoClient.Login.Reference;
            var logout = BanchoClient.Logout.Reference;
            var connect = BanchoClient.Connect.Reference;
            var send = BanchoClient.Send.Reference;
            _ = BanchoClient.Connected.Reference;
            _ = BanchoClient.Password.Reference;
            var harmony = new Harmony("osu-stable-plus.offline-session");
            // A retained password object is not proof of an active session. Use the
            // same logout action as the options menu, on the game thread, instead of
            // refusing to activate. Let native logout send its disconnect first.
            runOnGameThread(() =>
            {
                Console.WriteLine("[Offline mode] Installing login and connection guards on the game thread.");
                harmony.Patch(login, prefix: new HarmonyMethod(typeof(OfflineSession), nameof(BlockLogin)));
                harmony.Patch(connect, prefix: new HarmonyMethod(typeof(OfflineSession), nameof(BlockConnection)));
                Console.WriteLine($"[Offline mode] Before logout: connected={BanchoClient.Connected.Get()}, credentialsPresent={BanchoClient.Password.Get() != null}");
                logout.Invoke(GameBase.Options.Get(), null);
                Console.WriteLine("[Offline mode] Logout completed; installing send guard.");
                harmony.Patch(send, prefix: new HarmonyMethod(typeof(OfflineSession), nameof(BlockConnection)));
                EnsureLoggedOut();
                Console.WriteLine("[Offline mode] Ready.");
            });
            installed = true;
            return true;
        }
    }

    private static void EnsureLoggedOut()
    {
        if (BanchoClient.Connected.Get() || BanchoClient.Password.Get() != null)
            throw new InvalidOperationException(Product.Name + " could not finish logging out. Gameplay patches were not activated. Restart osu! and try again.");
    }

    private static void RunOnGameThread(Action action)
    {
        var completion = new TaskCompletionSource<bool>();
        var callback = VoidDelegate.MakeInstance(() =>
        {
            try { action(); completion.SetResult(true); }
            catch (Exception e) { completion.SetException(e); }
        });
        Scheduler.Add.Invoke(GameBase.Scheduler.Get(), [callback, false]);
        // Add runs immediately when already on the game thread. Otherwise give the
        // scheduler time to complete the native UI/logout work before patching gameplay.
        try
        {
            if (!completion.Task.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("osu! did not process the logout request. Gameplay patches were not activated.");
        }
        catch (AggregateException e) { throw e.GetBaseException(); }
    }

    private static bool BlockConnection() => false;

    private static bool BlockLogin()
    {
        try { Notifications.ShowMessage(LoginBlockedMessage, NotificationColor.Warning, 8000); }
        catch (Exception e) { Console.WriteLine($"[Offline mode] {e.Message}"); }
        return false;
    }
}
