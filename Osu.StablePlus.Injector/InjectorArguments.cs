using System;

namespace Osu.StablePlus.Injector;

internal enum InjectorMode
{
    Attach,
    Launch,
    ProcessId
}

internal sealed class InjectorArguments
{
    private InjectorArguments(InjectorMode mode, string? osuPath = null, int processId = 0)
    {
        Mode = mode;
        OsuPath = osuPath;
        ProcessId = processId;
    }

    public InjectorMode Mode { get; }
    public string? OsuPath { get; }
    public int ProcessId { get; }

    public static InjectorArguments Parse(string[] args)
    {
        if (args.Length == 0) return new InjectorArguments(InjectorMode.Attach);

        if (args.Length == 2 && args[0] == "--launch" && !string.IsNullOrWhiteSpace(args[1]))
            return new InjectorArguments(InjectorMode.Launch, args[1]);

        if (args.Length == 2 && args[0] == "--pid" && int.TryParse(args[1], out var id) && id > 0)
            return new InjectorArguments(InjectorMode.ProcessId, processId: id);

        throw new ArgumentException($"Usage: {Product.Executable} [--launch <path-to-osu!.exe> | --pid <process-id>]");
    }
}
