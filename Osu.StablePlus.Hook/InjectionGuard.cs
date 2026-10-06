using System;
using System.Linq;

namespace Osu.StablePlus.Hook;

internal static class InjectionGuard
{
    // Shared across renamed assemblies; legacy builds predate this guard, so
    // also check for their loaded hook assembly before installing any patches.
    private const string Key = "osu-stable-plus.active-hook";
    internal static bool IsHookAssembly(string? name) =>
        string.Equals(name, "osu!.hook", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, Product.HookAssembly, StringComparison.OrdinalIgnoreCase);

    internal static bool TryAcquire()
    {
        var domain = AppDomain.CurrentDomain;
        lock (domain)
        {
            if (domain.GetData(Key) != null || domain.GetAssemblies().Any(a =>
                a != typeof(InjectionGuard).Assembly && IsHookAssembly(a.GetName().Name))) return false;
            domain.SetData(Key, Product.HookAssembly);
            return true;
        }
    }
    internal static void Release()
    {
        lock (AppDomain.CurrentDomain) AppDomain.CurrentDomain.SetData(Key, null);
    }
}
