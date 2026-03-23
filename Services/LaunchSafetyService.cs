using System.Diagnostics;
using System.IO;

namespace WessTools.Services;

public static class LaunchSafetyService
{
    private static readonly string[] BlockedKeywords =
    [
        "install",
        "installer",
        "setup",
        "update",
        "updater",
        "unins",
        "uninstall",
        "bootstrap",
        "prereq",
        "redist",
        "redistributable"
    ];

    public static bool IsSafe(string executablePath, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            reason = "The app target does not exist anymore.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Only executable app targets are allowed.";
            return false;
        }

        var fullPath = Path.GetFullPath(executablePath);
        var lowerPath = fullPath.ToLowerInvariant();

        if (lowerPath.Contains("\\downloads\\", StringComparison.Ordinal) ||
            lowerPath.Contains("\\temp\\", StringComparison.Ordinal) ||
            lowerPath.Contains("\\appdata\\local\\temp\\", StringComparison.Ordinal))
        {
            reason = "Executables from Downloads or Temp are blocked.";
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
        if (BlockedKeywords.Any(keyword => fileName.Contains(keyword, StringComparison.Ordinal)))
        {
            reason = "This executable looks like an installer or uninstaller.";
            return false;
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
            var metadata = string.Join(' ',
                versionInfo.FileDescription ?? string.Empty,
                versionInfo.ProductName ?? string.Empty,
                versionInfo.OriginalFilename ?? string.Empty).ToLowerInvariant();

            if (BlockedKeywords.Any(keyword => metadata.Contains(keyword, StringComparison.Ordinal)))
            {
                reason = "The executable metadata looks like an installer or updater.";
                return false;
            }
        }
        catch
        {
        }

        var trustedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .ToArray();

        if (trustedRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (fullPath.Contains("\\games\\", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains("\\steam", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains("\\epic games\\", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains("\\riot games\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        reason = "This executable is outside common installed-app locations, so it stays blocked.";
        return false;
    }
}
