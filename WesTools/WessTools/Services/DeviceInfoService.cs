using System.Diagnostics;
using System.IO;
using System.Text;

namespace WessTools.Services;

public sealed class DeviceInfoService
{
    public string BuildSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"PC Name: {Environment.MachineName}");
        builder.AppendLine($"User: {Environment.UserName}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        builder.AppendLine($"Processor Count: {Environment.ProcessorCount}");
        builder.AppendLine($"System Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):dd\\.hh\\:mm\\:ss}");
        builder.AppendLine();

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var totalGb = drive.TotalSize / 1024d / 1024d / 1024d;
            builder.AppendLine($"{drive.Name} - {drive.DriveFormat} - {freeGb:0.0} GB free / {totalGb:0.0} GB");
        }

        return builder.ToString().Trim();
    }
}
