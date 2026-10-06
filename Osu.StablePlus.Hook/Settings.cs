using System;
using System.Diagnostics;
using System.IO;
using System.Xml.Serialization;

namespace Osu.StablePlus.Hook;

/// <summary>
///     (De)serializable settings model to handle loading and saving settings to disk.
/// </summary>
[Serializable]
public class Settings
{
    private static readonly XmlSerializer Serializer = new(typeof(Settings));

    /// <summary>
    ///     An instance of settings representing the default values.
    ///     This should not be modified at runtime.
    /// </summary>
    [XmlIgnore]
    public static readonly Settings Default = new();

    /// <summary>
    ///     Handles reading the patcher options from disk.
    /// </summary>
    public static Settings ReadFromDisk(string osuDir)
    {
        Debug.WriteLine("Reading patcher config from disk");

        var file = Path.Combine(osuDir, Product.SettingsFilename);
        if (!File.Exists(file)) file = Path.Combine(osuDir, Product.LegacySettingsFilename);

        if (!File.Exists(file))
            return Default;

        try
        {
            using var fs = File.OpenRead(file);
            return (Settings)Serializer.Deserialize(fs);
        }
        catch (Exception e)
        {
            // The next save replaces the file with defaults; keep the unreadable copy for recovery.
            var backup = file + ".invalid";
            try { File.Copy(file, backup, true); }
            catch (Exception copyError) { backup = $"(backup failed: {copyError.Message})"; }
            Console.WriteLine($"[Settings] Unable to read {file}; using defaults. Original kept at {backup}. {e}");
            return Default;
        }
    }

    /// <summary>
    ///     Handles writing the patcher options to disk.
    /// </summary>
    public static void WriteToDisk(Settings settings, string osuDir)
    {
        Debug.WriteLine("Writing patcher config to disk");

        Directory.CreateDirectory(osuDir);

        var file = Path.Combine(osuDir, Product.SettingsFilename);
        var temp = file + ".tmp";

        try
        {
            // Serialize to a separate file first so a failed save never truncates the existing settings.
            using (var fs = File.Create(temp))
                Serializer.Serialize(fs, settings);

            if (!File.Exists(file))
                File.Move(temp, file);
            else
            {
                try { File.Replace(temp, file, null); }
                catch (Exception e) when (e is IOException or PlatformNotSupportedException)
                {
                    // Some filesystems (e.g. under Wine) cannot replace; the complete copy is already on disk.
                    File.Copy(temp, file, true);
                    File.Delete(temp);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Settings] Unable to save {file}: {e}");
            try { File.Delete(temp); }
            catch (IOException) { }
        }
    }

    #region Options

    public bool EnableModAudioPreview { get; set; } = true;
    public bool AdjustStableScoring { get; set; } = true;
    public double DoubleTimeSpeed { get; set; } = 1.5;
    public bool DoubleTimeAdjustPitch { get; set; }
    public double HalfTimeSpeed { get; set; } = 0.75;
    public bool HalfTimeAdjustPitch { get; set; }
    public bool ShowRelaxAsRx { get; set; } = true;
    public bool ShowRateInModLabels { get; set; } = true;
    public bool ShowDifficultyInModLabels { get; set; } = true;
    public bool ShowMirrorInModLabels { get; set; } = true;
    public bool ApplyRelaxStarReduction { get; set; }
    public bool ApplyAutopilotStarReduction { get; set; }
    public int GameplayModIconOpacity { get; set; }
    public bool ShowPerformanceInGame { get; set; } = true;
    public bool ShowPerformanceOnLeaderboard { get; set; } = true;
    public bool EstimatePpWithoutRelax { get; set; } = true;
    public bool EstimatePpWithoutAutopilot { get; set; } = true;
    public int PerformanceDecimalPlaces { get; set; }

    #endregion
}
