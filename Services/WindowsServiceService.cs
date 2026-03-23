using System.Diagnostics;
using System.Text.RegularExpressions;
using WessTools.Models;

namespace WessTools.Services;

public sealed class WindowsServiceService
{
    public IReadOnlyList<ServiceEntry> LoadServices()
    {
        List<ServiceSnapshot> entries;
        try
        {
            var queryOutput = RunScCommand("query state= all");
            entries = ParseServiceNamesAndStates(queryOutput);
        }
        catch
        {
            return [];
        }

        var services = new List<ServiceEntry>();

        foreach (var entry in entries)
        {
            string detailsOutput;
            try
            {
                detailsOutput = RunScCommand($"qc \"{entry.ServiceName}\"");
            }
            catch
            {
                detailsOutput = string.Empty;
            }

            services.Add(new ServiceEntry
            {
                ServiceName = entry.ServiceName,
                DisplayName = entry.DisplayName,
                Description = "Browse and control Windows services from the deck.",
                StartMode = ParseStartMode(detailsOutput),
                StatusText = entry.StatusText,
                CanStop = string.Equals(entry.StatusText, "Running", StringComparison.OrdinalIgnoreCase)
            });
        }

        return services
            .OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void StartService(string serviceName) => RunScCommand($"start \"{serviceName}\"");

    public void StopService(string serviceName) => RunScCommand($"stop \"{serviceName}\"");

    public void RestartService(string serviceName)
    {
        StopService(serviceName);
        StartService(serviceName);
    }

    private static List<ServiceSnapshot> ParseServiceNamesAndStates(string output)
    {
        var services = new List<ServiceSnapshot>();
        var blocks = output.Split("SERVICE_NAME:", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
            {
                continue;
            }

            var serviceName = lines[0].Trim();
            var displayName = serviceName;
            var statusText = "Unknown";

            foreach (var line in lines)
            {
                if (line.StartsWith("DISPLAY_NAME", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0)
                    {
                        displayName = line[(idx + 1)..].Trim();
                    }
                }

                if (line.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"STATE\s*:\s*\d+\s+(.+)$");
                    if (match.Success)
                    {
                        statusText = ToTitleCase(match.Groups[1].Value.Trim().Replace('_', ' '));
                    }
                }
            }

            services.Add(new ServiceSnapshot(serviceName, displayName, statusText));
        }

        return services;
    }

    private static string ParseStartMode(string qcOutput)
    {
        foreach (var rawLine in qcOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("START_TYPE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idx = line.IndexOf(':');
            if (idx < 0)
            {
                continue;
            }

            var value = line[(idx + 1)..].Trim();
            var pieces = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length > 1)
            {
                return ToTitleCase(string.Join(' ', pieces.Skip(1)).Replace('_', ' '));
            }
        }

        return "Unknown";
    }

    private static string RunScCommand(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start sc.exe.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }

        return output;
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return string.Join(' ', value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private sealed record ServiceSnapshot(string ServiceName, string DisplayName, string StatusText);
}
