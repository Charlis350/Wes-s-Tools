using System.IO;
using Microsoft.Win32;
using WessTools.Models;

namespace WessTools.Services;

public sealed class StartupAppService
{
    public IReadOnlyList<StartupAppEntry> LoadStartupApps()
    {
        var items = new List<StartupAppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LoadRegistryItems(items, seen, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Current User - Run");
        LoadRegistryItems(items, seen, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "Local Machine - Run");
        LoadStartupFolderItems(items, seen, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup Folder");
        LoadStartupFolderItems(items, seen, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup Folder");

        return items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void LoadRegistryItems(List<StartupAppEntry> items, HashSet<string> seen, RegistryKey hive, string subKeyPath, string sourceLabel)
    {
        try
        {
            using var key = hive.OpenSubKey(subKeyPath);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not string command || string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                var resolvedPath = ExtractExecutablePath(command);
                AddStartupItem(items, seen, valueName, command, resolvedPath, sourceLabel);
            }
        }
        catch
        {
        }
    }

    private static void LoadStartupFolderItems(List<StartupAppEntry> items, HashSet<string> seen, string folderPath, string sourceLabel)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                return;
            }

            foreach (var filePath in Directory.EnumerateFiles(folderPath))
            {
                var extension = Path.GetExtension(filePath);
                string? resolvedPath = null;

                if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedPath = ShortcutResolver.ResolveTarget(filePath);
                }
                else if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedPath = filePath;
                }

                var command = resolvedPath ?? filePath;
                AddStartupItem(items, seen, Path.GetFileNameWithoutExtension(filePath), command, resolvedPath, sourceLabel);
            }
        }
        catch
        {
        }
    }

    private static void AddStartupItem(List<StartupAppEntry> items, HashSet<string> seen, string name, string command, string? resolvedPath, string sourceLabel)
    {
        var normalizedPath = resolvedPath ?? string.Empty;
        var key = $"{name}|{normalizedPath}|{sourceLabel}";
        if (!seen.Add(key))
        {
            return;
        }

        items.Add(new StartupAppEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Startup Item" : name.Trim(),
            Command = command.Trim(),
            ResolvedPath = normalizedPath,
            SourceLabel = sourceLabel,
            TileGlyph = string.IsNullOrWhiteSpace(name) ? "S" : char.ToUpperInvariant(name[0]).ToString(),
            IconSource = !string.IsNullOrWhiteSpace(normalizedPath) ? IconExtractionService.TryExtractIcon(normalizedPath) : null
        });
    }

    private static string? ExtractExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.StartsWith('"'))
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            if (closingQuoteIndex > 1)
            {
                var quotedPath = trimmed[1..closingQuoteIndex];
                return File.Exists(quotedPath) ? quotedPath : null;
            }
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && File.Exists(parts[0]) ? parts[0] : null;
    }
}
