using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Osu.StablePlus.Stubs.Tests;

#pragma warning disable CS0618 // Type or member is obsolete

internal static class OsuLoader
{
    private static Task<Assembly>? _loadTask;

    /// <summary>
    ///     Download the latest osu! game file and load the assembly into the current process.
    /// </summary>
    public static Task<Assembly> UpdateAndLoad()
    {
        if (_loadTask != null)
            return _loadTask;

        if (Environment.Is64BitProcess)
            throw new Exception("Cannot load osu! into a 64-bit process!");

        return _loadTask = Task.Run(async () =>
        {
            Console.WriteLine("Updating osu!");

            var installedDirectory = Environment.GetEnvironmentVariable("OSU_PATH");
            var osuDir = installedDirectory ?? Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "osu!"));
            var osuExe = Path.Combine(osuDir, "osu!.exe");

            if (installedDirectory != null && !File.Exists(osuExe))
                throw new FileNotFoundException("OSU_PATH must contain osu!.exe", osuExe);

            if (installedDirectory == null && !File.Exists(osuExe)) // Retry an incomplete download too.
            {
                Directory.CreateDirectory(osuDir);
                await OsuApi.DownloadOsu(osuDir);
            }

            Console.WriteLine("Loading osu!.exe");

            // Add osu! directory to assembly search path
            AppDomain.CurrentDomain.AppendPrivatePath(osuDir);
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var dependency = Path.Combine(osuDir, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
            };

            // Load the osu! executable and it's dependencies as assemblies without executing
            return Assembly.LoadFile(osuExe);
        });
    }
}
