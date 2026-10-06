namespace Osu.StablePlus;

/// <summary>
///     Product identity. Replay-format keys keep their pre-rename "osu!patcher" names for compatibility
///     with saved replays, and the legacy settings file is still read when migrating from osu! patcher.
/// </summary>
public static class Product
{
    public const string Name = "osu!stable+";
    public const string Executable = "osu-stable-plus.exe";
    public const string HookAssembly = "osu-stable-plus-hook";
    public const string HookType = "Osu.StablePlus.Hook.Hook";
    public const string HelperExecutable = "osu-stable-plus-performance.exe";
    public const string SettingsFilename = "osu-stable-plus.xml";
    /// <summary>Upstream osu! patcher's settings file, read only when <see cref="SettingsFilename" /> is absent.</summary>
    public const string LegacySettingsFilename = "osu!patcher.xml";
    public const string LogFilename = "osu-stable-plus.log";
    public static string Version => typeof(Product).Assembly.GetName().Version!.ToString(3);
}
