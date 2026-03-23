using System.Diagnostics;
using System.IO;

namespace WessTools.Services;

public sealed class OptimizationService
{
    public string FlushDnsCache()
    {
        RunCommand("ipconfig", "/flushdns");
        return "DNS cache flushed. This can help resolve stale network routing data.";
    }

    public string EnableHighPerformancePowerPlan()
    {
        RunCommand("powercfg", "/SETACTIVE SCHEME_MIN");
        return "Windows switched to the High Performance power plan.";
    }

    public string CleanTempFiles()
    {
        var tempPath = Path.GetTempPath();
        var deletedCount = 0;

        foreach (var file in SafeEnumerateFiles(tempPath))
        {
            try
            {
                File.Delete(file);
                deletedCount++;
            }
            catch
            {
            }
        }

        return $"Cleaned {deletedCount} temp files.";
    }

    public string OpenGameModeSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:gaming-gamemode",
            UseShellExecute = true
        });
        return "Opened Windows Game Mode settings.";
    }

    private static void RunCommand(string fileName, string arguments)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"Could not start {fileName}.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string rootPath)
    {
        var stack = new Stack<string>();
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        stack.Push(rootPath);
        while (stack.Count > 0)
        {
            var current = stack.Pop();

            IEnumerable<string> files = [];
            try
            {
                files = Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories = [];
            try
            {
                directories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var directory in directories)
            {
                stack.Push(directory);
            }
        }
    }
}
