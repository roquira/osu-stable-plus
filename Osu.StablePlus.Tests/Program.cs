using NUnitLite;

namespace Osu.StablePlus.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        Hook.PatchingRuntime.Configure();
        return new AutoRun().Execute(args);
    }
}
