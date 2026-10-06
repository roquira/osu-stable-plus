using System;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace Osu.StablePlus.Hook;

internal static class PatchingRuntime
{
    private const string CompilerPatchId = "stableplus.compiler.names";
    private static readonly object ConfigurationLock = new();
    private static bool configured;
    private static bool resolverInstalled;
    private static FieldInfo wrapperName = null!;
    private static int generatedNames;
    internal static void Configure()
    {
        lock (ConfigurationLock)
        {
            if (configured) return;
            if (!resolverInstalled)
            {
                AppDomain.CurrentDomain.AssemblyResolve += ResolveLoadedAssembly;
                resolverInstalled = true;
            }
            // Harmony 2.3.1.1 merges MonoMod as internal types. Use its switch API,
            // not environment variables (which it snapshots on first access).
            // Cecil emits managed PE assemblies through the normal CLR loader.
            var harmonyAssembly = typeof(Harmony).Assembly;
            var switches = harmonyAssembly.GetType("MonoMod.Switches", true)!;
            var setter = switches.GetMethod("SetSwitchValue", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(object) }, null)
                ?? throw new MissingMethodException("MonoMod.Switches.SetSwitchValue");
            setter.Invoke(null, new object[] { "DMDType", "cecil" });

            // Copying stable's obfuscated names into generated wrappers reproduced
            // native compilation crashes in the running client. Give generated
            // methods their own names; original method identities stay unchanged.
            // These private bindings belong to the pinned Harmony build and are
            // deliberately checked before installing any game hooks.
            wrapperName = harmonyAssembly.GetType("MonoMod.Utils.DynamicMethodDefinition", true)!
                .GetField("<Name>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException("MonoMod.Utils.DynamicMethodDefinition", "<Name>k__BackingField");
            var createWrapper = harmonyAssembly.GetType("HarmonyLib.MethodPatcher", true)!
                .GetMethod("CreateDynamicMethod", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("HarmonyLib.MethodPatcher", "CreateDynamicMethod");
            new Harmony(CompilerPatchId).Patch(createWrapper,
                postfix: new HarmonyMethod(typeof(PatchingRuntime), nameof(NameGeneratedWrapper)));
            configured = true;
            Console.WriteLine($"[Runtime] CLR {Environment.Version}; Harmony {harmonyAssembly.GetName().Version}; code generation: cecil; unique wrapper names.");
        }
    }

    private static void NameGeneratedWrapper(object __result)
    {
        var name = "StablePlusWrapper_" + Interlocked.Increment(ref generatedNames);
        wrapperName.SetValue(__result, name);
    }

    // The injector loads the hook outside the default load context. Cecil's
    // generated assemblies reference it by identity. Reuse exact loaded identities;
    // do not probe paths or load another copy of a hook/dependency into the game.
    internal static Assembly? ResolveLoadedAssembly(object? sender, ResolveEventArgs args) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);

    internal static bool IsNativeFailure(Exception exception)
    {
        if (exception is SEHException or AccessViolationException) return true;
        if (exception is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                if (IsNativeFailure(inner)) return true;
        return exception.InnerException != null && IsNativeFailure(exception.InnerException);
    }
}
