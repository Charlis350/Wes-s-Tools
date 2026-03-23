using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace WessTools.Services;

public sealed class FileToolsService
{
    public string OpenTempFolder()
    {
        return OpenFolder(Path.GetTempPath());
    }

    public string OpenDownloadsFolder()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return OpenFolder(path);
    }

    public string OpenDesktopFolder()
    {
        return OpenFolder(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    }

    public string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static string OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });

        return path;
    }
}
