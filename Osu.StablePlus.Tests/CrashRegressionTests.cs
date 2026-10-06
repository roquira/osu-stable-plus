using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using NUnit.Framework;
using Osu.StablePlus.Hook;

namespace Osu.StablePlus.Tests;

[TestFixture, NonParallelizable]
public class CrashRegressionTests
{
    [Test]
    public void GeneratedWrappersUseLoadedAssembliesInsteadOfReflectionEmit()
    {
        PatchingRuntime.Configure();
        var type = typeof(Harmony).Assembly.GetType("MonoMod.Utils.DynamicMethodDefinition", true)!;
        using var definition = (IDisposable)Activator.CreateInstance(type,
            new object[] { typeof(CrashRegressionTests).GetMethod(nameof(Probe), BindingFlags.Static | BindingFlags.NonPublic)! })!;
        var method = (MethodInfo)type.GetMethod("Generate", Type.EmptyTypes)!.Invoke(definition, null)!;
        Assert.That(method, Is.Not.InstanceOf<DynamicMethod>());
        Assert.That(method.DeclaringType, Is.Not.Null);
        Assert.That(method.Module.Assembly.IsDynamic, Is.False, "MethodBuilder also failed in the live client; require Cecil's loaded assembly.");
        Assert.That(method.Invoke(null, new object[] { 4 }), Is.EqualTo(11));
    }

    [Test]
    public void CompilerReferencesResolveOnlyExactAlreadyLoadedIdentities()
    {
        var hook = typeof(Hook.Hook).Assembly;
        Assert.That(PatchingRuntime.ResolveLoadedAssembly(null, new ResolveEventArgs(hook.FullName)), Is.SameAs(hook));
        var differentIdentity = new AssemblyName(hook.FullName) { Version = new Version(99, 0, 0, 0) };
        Assert.That(PatchingRuntime.ResolveLoadedAssembly(null, new ResolveEventArgs(differentIdentity.FullName)), Is.Null);
        Assert.That(PatchingRuntime.ResolveLoadedAssembly(null, new ResolveEventArgs("missing-assembly")), Is.Null);
    }

    private static int Probe(int value) => value + 7;

    [Test]
    public void ObfuscatedOriginalsGetUniqueOrdinaryWrapperNamesWithoutChangingTheirIdentity()
    {
        PatchingRuntime.Configure();
        var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
            new AssemblyName("WrapperNameProbe_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("Probe").DefineType("#=zProbe$=", TypeAttributes.Public);
        foreach (var name in new[] { "#=zFirst$=", "#=zSecond$=" })
        {
            var method = builder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(int), new[] { typeof(int) });
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_7);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
        }
        var source = builder.CreateType();
        var harmony = new Harmony("stableplus.tests.wrapper-names");
        string? previousName = null;
        foreach (var original in source.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var originalName = original.Name;
            Assert.That(original.Invoke(null, new object[] { 4 }), Is.EqualTo(11));
            try
            {
                var wrapper = harmony.Patch(original, postfix: new HarmonyMethod(typeof(CrashRegressionTests), nameof(DoubleResult)));
                Assert.That(wrapper.Name, Does.Match("^StablePlusWrapper_[0-9]+$"));
                Assert.That(wrapper.Name, Is.Not.EqualTo(previousName));
                Assert.That(wrapper.Module.Assembly.IsDynamic, Is.False);
                Assert.That(original.Name, Is.EqualTo(originalName));
                Assert.That(original.Invoke(null, new object[] { 4 }), Is.EqualTo(22));
                previousName = wrapper.Name;
            }
            finally { harmony.Unpatch(original, HarmonyPatchType.All, harmony.Id); }
            Assert.That(original.Invoke(null, new object[] { 4 }), Is.EqualTo(11));
        }
    }

    private static void DoubleResult(ref int __result) => __result *= 2;

    [Test]
    public void ConfiguringTwiceDoesNotInstallDuplicateCompilerHooks()
    {
        PatchingRuntime.Configure();
        PatchingRuntime.Configure();
        var factory = typeof(Harmony).Assembly.GetType("HarmonyLib.MethodPatcher", true)!
            .GetMethod("CreateDynamicMethod", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.That(Harmony.GetPatchInfo(factory).Postfixes,
            Has.Exactly(1).Matches<Patch>(patch => patch.owner == "stableplus.compiler.names"));
    }

    [Test]
    public void NativeFailuresStopInstallationEvenInsideReflectionOrHarmonyWrappers()
    {
        Assert.That(PatchingRuntime.IsNativeFailure(new TargetInvocationException(new SEHException())), Is.True);
        Assert.That(PatchingRuntime.IsNativeFailure(new AggregateException(new Exception(), new AccessViolationException())), Is.True);
        Assert.That(PatchingRuntime.IsNativeFailure(new TargetInvocationException(new MissingMethodException())), Is.False);
    }

    [Test]
    public void LoggingPreservesPreviousSessionsAndFlushesMultilineCrashDetails()
    {
        var directory = Path.GetFullPath(Path.Combine(".scratch", "log-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, Product.LogFilename);
            File.WriteAllText(path, "previous un-timestamped crash");
            using (var log = SessionLog.Open(directory))
            {
                log.WriteLine("failure\nstack frame → details");
                using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                var current = reader.ReadToEnd();
                Assert.That(current, Does.Contain("failure"));
                Assert.That(current, Does.Contain("stack frame → details"));
                Assert.That(current.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries), Has.All.StartsWith("["));
            }
            var previous = File.ReadAllText(path);
            using (var log = SessionLog.Open(directory)) log.WriteLine("second session");
            var archives = Directory.GetFiles(Path.Combine(directory, "Logs", "osu-stable-plus"), "*.log");
            Assert.That(archives.Length, Is.EqualTo(2));
            Assert.That(Array.ConvertAll(archives, File.ReadAllText), Does.Contain(previous));
            Assert.That(Array.ConvertAll(archives, File.ReadAllText), Does.Contain("previous un-timestamped crash"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("stack frame"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
