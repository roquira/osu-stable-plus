using System;
using System.IO;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Threading;
using HoLLy.ManagedInjector;

namespace Osu.StablePlus.Injector;

[SupportedOSPlatform("windows")]
internal static class Injector
{
    private static readonly TimeSpan RuntimeWaitTimeout = TimeSpan.FromSeconds(30);

    public static int Main(string[] args)
    {
        try
        {
            var request = InjectorArguments.Parse(args);
            var pid = GetOsuPid(request);
            WaitForRuntime(pid);

            using var proc = new InjectableProcess(pid);
            var dllPath = Path.Combine(Path.GetDirectoryName(typeof(Injector).Assembly.Location)!, Product.HookAssembly + ".dll");

            proc.Inject(dllPath, Product.HookType, "Initialize");
            Console.WriteLine(Product.Name + " injection requested. Check osu! for loading status.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);

            if (args.Length == 0 || (args.Length > 0 && args[0] == "--launch"))
            {
                Console.WriteLine("\nPress any key to continue...");
                Console.Write("\a");
                Console.ReadKey();
            }
            return 1;
        }
    }

    /// <summary>
    ///     Find osu!. The hook checks live login state and installs offline protection
    ///     before applying gameplay patches.
    /// </summary>
    /// <returns>The process id of the first matching process.</returns>
    /// <exception cref="Exception">If found invalid osu! process or no process at all.</exception>
    private static uint GetOsuPid(InjectorArguments request)
    {
        if (request.Mode == InjectorMode.ProcessId)
        {
            using var target = Process.GetProcessById(request.ProcessId);
            if (!target.ProcessName.Equals("osu!", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Selected process is not osu!.");
            return checked((uint)request.ProcessId);
        }

        if (request.Mode == InjectorMode.Launch) return LaunchOsu(request.OsuPath!);

        return FindRunningOsuPid() ?? throw new Exception("Cannot find a running osu! process!");
    }

    private static uint LaunchOsu(string requestedPath)
    {
        var osuPath = Path.GetFullPath(requestedPath);
        if (!Path.GetFileName(osuPath).Equals("osu!.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The launch path must point to osu!.exe.", nameof(requestedPath));
        if (!File.Exists(osuPath)) throw new FileNotFoundException("Cannot find osu!.exe.", osuPath);
        if (FindRunningOsuPid().HasValue)
            throw new InvalidOperationException("osu! is already running. Close it before using the launch shortcut.");

        var workingDirectory = Path.GetDirectoryName(osuPath)
                               ?? throw new ArgumentException("The osu! path has no installation directory.", nameof(requestedPath));
        Console.WriteLine($"Launching {osuPath}");

        using var process = Process.Start(new ProcessStartInfo(osuPath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Windows did not start osu!.");

        return checked((uint)process.Id);
    }

    private static uint? FindRunningOsuPid()
    {
        using var mgmt = new ManagementClass("Win32_Process");
        using var processes = mgmt.GetInstances();

        foreach (var process in processes)
        {
            var exe = (string)process["Name"];
            var pid = (uint)process["ProcessId"];

            if (!exe.Equals("osu!.exe", StringComparison.OrdinalIgnoreCase)) continue;

            return pid;
        }

        return null;
    }

    private static void WaitForRuntime(uint pid)
    {
        var deadline = DateTime.UtcNow + RuntimeWaitTimeout;
        Console.WriteLine("Waiting for osu! to become ready...");

        while (DateTime.UtcNow < deadline)
        {
            using (var process = Process.GetProcessById(checked((int)pid)))
            {
                if (process.HasExited) throw new InvalidOperationException("osu! exited before it could be patched.");
            }

            using var injectable = new InjectableProcess(pid);
            switch (injectable.GetStatus())
            {
                case ProcessStatus.Ok:
                    return;
                case ProcessStatus.ArchitectureMismatch:
                    throw new InvalidOperationException("The patcher and osu! process architectures are incompatible.");
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("osu! did not load its .NET runtime within 30 seconds.");
    }
}
