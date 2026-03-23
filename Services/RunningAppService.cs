using System.Diagnostics;
using WessTools.Models;

namespace WessTools.Services;

public sealed class RunningAppService
{
    public IReadOnlyList<RunningAppEntry> LoadRunningApps()
    {
        var apps = new List<RunningAppEntry>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId ||
                    process.MainWindowHandle == IntPtr.Zero ||
                    string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    continue;
                }

                var executablePath = TryGetMainModulePath(process);
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    continue;
                }

                apps.Add(new RunningAppEntry
                {
                    Name = process.ProcessName,
                    ProcessId = process.Id,
                    WindowTitle = process.MainWindowTitle,
                    ExecutablePath = executablePath,
                    MemoryText = $"{Math.Max(1, process.WorkingSet64 / 1024 / 1024)} MB RAM",
                    IconSource = IconExtractionService.TryExtractIcon(executablePath)
                });
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return apps
            .OrderByDescending(app => ParseMemory(app.MemoryText))
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void CloseApp(RunningAppEntry entry)
    {
        using var process = Process.GetProcessById(entry.ProcessId);
        if (!process.CloseMainWindow())
        {
            throw new InvalidOperationException("That app does not expose a normal main-window close action.");
        }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static int ParseMemory(string memoryText)
    {
        var digits = new string(memoryText.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }
}
