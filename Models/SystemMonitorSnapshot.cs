namespace WessTools.Models;

public sealed class SystemMonitorSnapshot
{
    public double CpuPercent { get; init; }

    public double MemoryPercent { get; init; }

    public double UsedMemoryGb { get; init; }

    public double TotalMemoryGb { get; init; }

    public double DownloadMbps { get; init; }

    public double UploadMbps { get; init; }

    public int ProcessCount { get; init; }
}
