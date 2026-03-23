using Microsoft.Win32;
using System.IO;
using WessTools.Models;

namespace WessTools.Services;

public sealed class AppDiscoveryService
{
    public IReadOnlyList<LaunchableApp> Scan()
    {
        var apps = new Dictionary<string, LaunchableApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in LoadFromStartMenu())
        {
            apps.TryAdd(app.ExecutablePath, app);
        }

        foreach (var app in LoadFromRegistry())
        {
            apps.TryAdd(app.ExecutablePath, app);
        }

        foreach (var app in LoadFromAppPaths())
        {
            apps.TryAdd(app.ExecutablePath, app);
        }

        foreach (var app in LoadFromKnownInstallFolders())
        {
            apps.TryAdd(app.ExecutablePath, app);
        }

        return apps.Values
            .OrderByDescending(app => app.IsGame)
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<LaunchableApp> LoadFromStartMenu()
    {
        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));

        foreach (var folder in folders)
        {
            foreach (var shortcutPath in SafeEnumerateFiles(folder, "*.lnk"))
            {
                var target = ShortcutResolver.ResolveTarget(shortcutPath);
                if (target is null || !LaunchSafetyService.IsSafe(target, out _))
                {
                    continue;
                }

                yield return CreateApp(
                    Path.GetFileNameWithoutExtension(shortcutPath),
                    target,
                    "Shortcut",
                    "Windows shortcut",
                    Path.GetDirectoryName(target));
            }
        }
    }

    private static IEnumerable<LaunchableApp> LoadFromRegistry()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? uninstallKey = null;
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                }
                catch
                {
                }

                if (uninstallKey is null)
                {
                    continue;
                }

                using (uninstallKey)
                {
                    foreach (var name in uninstallKey.GetSubKeyNames())
                    {
                        using var appKey = uninstallKey.OpenSubKey(name);
                        if (appKey is null)
                        {
                            continue;
                        }

                        var displayName = appKey.GetValue("DisplayName") as string;
                        var executablePath = NormalizePath(appKey.GetValue("DisplayIcon") as string);
                        if (string.IsNullOrWhiteSpace(displayName) || executablePath is null || !LaunchSafetyService.IsSafe(executablePath, out _))
                        {
                            continue;
                        }

                        yield return CreateApp(
                            displayName,
                            executablePath,
                            "Installed app catalog",
                            appKey.GetValue("Publisher") as string ?? "Installed app",
                            appKey.GetValue("InstallLocation") as string);
                    }
                }
            }
        }
    }

    private static IEnumerable<LaunchableApp> LoadFromAppPaths()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? appPaths = null;
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    appPaths = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                }
                catch
                {
                }

                if (appPaths is null)
                {
                    continue;
                }

                using (appPaths)
                {
                    foreach (var subKeyName in appPaths.GetSubKeyNames())
                    {
                        using var appKey = appPaths.OpenSubKey(subKeyName);
                        if (appKey is null)
                        {
                            continue;
                        }

                        var executablePath = NormalizePath((appKey.GetValue(string.Empty) as string) ?? (appKey.GetValue("Path") as string));
                        if (executablePath is null || !LaunchSafetyService.IsSafe(executablePath, out _))
                        {
                            continue;
                        }

                        yield return CreateApp(
                            GuessDisplayName(executablePath),
                            executablePath,
                            "Windows App Paths",
                            "Registered application",
                            Path.GetDirectoryName(executablePath));
                    }
                }
            }
        }
    }

    private static IEnumerable<LaunchableApp> LoadFromKnownInstallFolders()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Opera"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Opera")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));

        var candidateExecutableNames = new[]
        {
            "opera.exe",
            "launcher.exe",
            "chrome.exe",
            "firefox.exe",
            "msedge.exe",
            "discord.exe",
            "steam.exe",
            "epicgameslauncher.exe",
            "brave.exe",
            "vivaldi.exe"
        };

        foreach (var root in roots)
        {
            foreach (var executableName in candidateExecutableNames)
            {
                foreach (var executablePath in SafeEnumerateFiles(root, executableName))
                {
                    if (!LooksLikePrimaryAppExecutable(executablePath) || !LaunchSafetyService.IsSafe(executablePath, out _))
                    {
                        continue;
                    }

                    yield return CreateApp(
                        GuessDisplayName(executablePath),
                        executablePath,
                        "Installed programs folder",
                        "Filesystem discovery",
                        Path.GetDirectoryName(executablePath));
                }
            }
        }
    }

    private static string? NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var cleaned = rawPath.Trim().Trim('"');
        var commaIndex = cleaned.IndexOf(',');
        if (commaIndex >= 0)
        {
            cleaned = cleaned[..commaIndex];
        }

        if (Directory.Exists(cleaned))
        {
            return SafeEnumerateFiles(cleaned, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Length)
                .FirstOrDefault(LooksLikePrimaryAppExecutable);
        }

        return File.Exists(cleaned) ? cleaned : null;
    }

    private static bool LooksLikePrimaryAppExecutable(string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
        if (fileName is "setup" or "installer" or "updater" or "unins000" or "notification_helper")
        {
            return false;
        }

        var parent = Path.GetFileName(Path.GetDirectoryName(executablePath) ?? string.Empty).ToLowerInvariant().Replace(" ", string.Empty);
        var normalized = fileName.Replace(" ", string.Empty);

        return normalized == parent ||
               normalized.Contains("opera", StringComparison.Ordinal) ||
               normalized.Contains("gx", StringComparison.Ordinal) ||
               normalized.Contains("chrome", StringComparison.Ordinal) ||
               normalized.Contains("firefox", StringComparison.Ordinal) ||
               normalized.Contains("discord", StringComparison.Ordinal) ||
               normalized.Contains("steam", StringComparison.Ordinal) ||
               normalized.Contains("epic", StringComparison.Ordinal) ||
               normalized.Contains("launcher", StringComparison.Ordinal) ||
               normalized.Contains("game", StringComparison.Ordinal);
    }

    private static string GuessDisplayName(string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath);
        if (fileName.Equals("opera", StringComparison.OrdinalIgnoreCase) &&
            executablePath.Contains("opera gx", StringComparison.OrdinalIgnoreCase))
        {
            return "Opera GX";
        }

        return fileName.Replace('_', ' ');
    }

    private static LaunchableApp CreateApp(string name, string executablePath, string sourceLabel, string publisher, string? installLocation)
    {
        return new LaunchableApp
        {
            Name = name.Trim(),
            ExecutablePath = executablePath,
            SourceLabel = sourceLabel,
            Publisher = publisher,
            InstallLocation = installLocation,
            IsGame = LooksLikeGame(name, executablePath, installLocation, publisher),
            IconSource = IconExtractionService.TryExtractIcon(executablePath)
        };
    }

    private static bool LooksLikeGame(string name, string executablePath, string? installLocation, string publisher)
    {
        var text = string.Join(' ', name, executablePath, installLocation ?? string.Empty, publisher).ToLowerInvariant();
        return text.Contains("game", StringComparison.Ordinal) ||
               text.Contains("steam", StringComparison.Ordinal) ||
               text.Contains("epic", StringComparison.Ordinal) ||
               text.Contains("ubisoft", StringComparison.Ordinal) ||
               text.Contains("riot", StringComparison.Ordinal) ||
               text.Contains("battle.net", StringComparison.Ordinal) ||
               text.Contains("rockstar", StringComparison.Ordinal) ||
               text.Contains("\\games\\", StringComparison.Ordinal);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string rootPath, string pattern, SearchOption option = SearchOption.AllDirectories)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            yield break;
        }

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files = [];
            try
            {
                files = Directory.EnumerateFiles(current, pattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var file in files)
            {
                yield return file;
            }

            if (option == SearchOption.TopDirectoryOnly)
            {
                continue;
            }

            IEnumerable<string> subDirectories = [];
            try
            {
                subDirectories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
            }

            foreach (var subDirectory in subDirectories)
            {
                stack.Push(subDirectory);
            }
        }
    }
}
