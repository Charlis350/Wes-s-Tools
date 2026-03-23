namespace WessTools.Models;

public sealed class GameProfilePreset
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool EnableHighPerformance { get; init; }

    public bool FlushDns { get; init; }

    public bool CleanTempFiles { get; init; }

    public LaunchableApp? TargetApp { get; set; }

    public string TargetAppName => TargetApp?.Name ?? "No game selected";
}
