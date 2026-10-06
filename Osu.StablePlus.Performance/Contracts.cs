namespace Osu.StablePlus.Performance;

// Shared wire contract. No osu! or runtime-specific types cross the process boundary.
public sealed class CalculationRequest
{
    public const int ProtocolVersion = 3;
    public const string EngineVersion = "2026.730.0";
    public int Protocol { get; set; } = ProtocolVersion;
    public long Id { get; set; }
    public string MapPath { get; set; } = "";
    public int Mode { get; set; }
    public int? SourceMode { get; set; }
    public int Mods { get; set; }
    public double Rate { get; set; } = 1;
    public double?[] Difficulty { get; set; } = new double?[4]; // HP, CS, AR, OD before HR/EZ
    public string? Mirror { get; set; }
    public bool NativeLazer { get; set; }
    public bool Classic { get; set; }
    public bool? ClassicNoSliderHeadAccuracy { get; set; }
    public bool StableEstimate { get; set; }
    public bool WithoutRelax { get; set; }
    public bool WithoutAutopilot { get; set; }
    public bool StarsOnly { get; set; }
    public bool Maximum { get; set; }
    public bool Partial { get; set; }
    public bool AllowPartial { get; set; }
    public int N300 { get; set; }
    public int N100 { get; set; }
    public int N50 { get; set; }
    public int Misses { get; set; }
    public int Geki { get; set; }
    public int Katu { get; set; }
    // Map-time frontier, independent of judgement/bonus counters.
    public double? ProgressTime { get; set; }
    public System.Collections.Generic.Dictionary<string, int>? Statistics { get; set; }
    public System.Collections.Generic.Dictionary<string, int>? MaximumStatistics { get; set; }
    // Native difficulty-relevant objects: start, end, position/column. Used to
    // validate conversions; never used to manufacture judgement statistics.
    public double[][]? ObjectStructure { get; set; }
    public string? ConversionError { get; set; }
    public int Combo { get; set; }
    public int TickHits { get; set; }
    public int TickMisses { get; set; }
    public int TailHits { get; set; }
    public int SmallTickHits { get; set; }
    public int SmallTickMisses { get; set; }
    public long? LegacyTotal { get; set; }
    public bool VanillaScoring { get; set; }
}

public sealed class CalculationResult
{
    public int Protocol { get; set; } = CalculationRequest.ProtocolVersion;
    public string Version { get; set; } = CalculationRequest.EngineVersion;
    public long Id { get; set; }
    public string? Error { get; set; }
    public double Pp { get; set; }
    public double Stars { get; set; }
    public double Aim { get; set; }
    public double Speed { get; set; }
    public double Accuracy { get; set; }
    public double Reading { get; set; }
    public double Flashlight { get; set; }
    public double EffectiveMisses { get; set; }
    public double Difficulty { get; set; }
    public double? EstimatedUnstableRate { get; set; }
    public int MaxCombo { get; set; }
    public bool Hypothetical { get; set; }
    public string Semantics { get; set; } = "stable/classic";
    public string MissMethod { get; set; } = "combo-based";
}
