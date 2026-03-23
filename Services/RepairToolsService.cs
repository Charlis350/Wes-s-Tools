using System.Diagnostics;

namespace WessTools.Services;

public sealed class RepairToolsService
{
    public string RunSystemFileChecker()
    {
        StartElevatedCommand("sfc /scannow");
        return "Started System File Checker in an elevated command window.";
    }

    public string RunDismRestoreHealth()
    {
        StartElevatedCommand("DISM /Online /Cleanup-Image /RestoreHealth");
        return "Started DISM RestoreHealth in an elevated command window.";
    }

    public string OpenWindowsUpdate()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:windowsupdate",
            UseShellExecute = true
        });

        return "Opened Windows Update settings.";
    }

    private static void StartElevatedCommand(string command)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k {command}",
            UseShellExecute = true,
            Verb = "runas"
        });
    }
}
