using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WessTools.Models;

namespace WessTools.Services;

public sealed class SystemMonitorService
{
    private readonly PerformanceCounter _cpuCounter = new("Processor", "% Processor Time", "_Total");
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastSampleTime = DateTime.UtcNow;

    public SystemMonitorService()
    {
        _cpuCounter.NextValue();
        PrimeNetworkCounters();
    }

    public SystemMonitorSnapshot ReadSnapshot()
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Max(0.25, (now - _lastSampleTime).TotalSeconds);
        var networkTotals = GetNetworkTotals();
        var deltaReceived = Math.Max(0, networkTotals.BytesReceived - _lastBytesReceived);
        var deltaSent = Math.Max(0, networkTotals.BytesSent - _lastBytesSent);

        _lastBytesReceived = networkTotals.BytesReceived;
        _lastBytesSent = networkTotals.BytesSent;
        _lastSampleTime = now;

        var memoryStatus = new MemoryStatusEx();
        GlobalMemoryStatusEx(memoryStatus);
        var totalMemory = memoryStatus.TotalPhys / 1024d / 1024d / 1024d;
        var availableMemory = memoryStatus.AvailPhys / 1024d / 1024d / 1024d;
        var usedMemory = Math.Max(0, totalMemory - availableMemory);

        return new SystemMonitorSnapshot
        {
            CpuPercent = Math.Clamp(Math.Round(_cpuCounter.NextValue(), 1), 0, 100),
            TotalMemoryGb = Math.Round(totalMemory, 1),
            UsedMemoryGb = Math.Round(usedMemory, 1),
            MemoryPercent = totalMemory <= 0 ? 0 : Math.Round(usedMemory / totalMemory * 100, 1),
            DownloadMbps = Math.Round(deltaReceived * 8 / elapsedSeconds / 1_000_000d, 2),
            UploadMbps = Math.Round(deltaSent * 8 / elapsedSeconds / 1_000_000d, 2),
            ProcessCount = Process.GetProcesses().Length
        };
    }

    private void PrimeNetworkCounters()
    {
        var totals = GetNetworkTotals();
        _lastBytesReceived = totals.BytesReceived;
        _lastBytesSent = totals.BytesSent;
        _lastSampleTime = DateTime.UtcNow;
    }

    private static (long BytesReceived, long BytesSent) GetNetworkTotals()
    {
        long received = 0;
        long sent = 0;

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var stats = adapter.GetIPv4Statistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
        }

        return (received, sent);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
