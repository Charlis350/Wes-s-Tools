using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace WessTools.Services;

public sealed class CaptureService
{
    private readonly string _captureFolder;

    public CaptureService()
    {
        _captureFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "WessTools Captures");
        Directory.CreateDirectory(_captureFolder);
    }

    public string TakeScreenshot()
    {
        var bounds = GetPrimaryScreenBounds();
        var filePath = Path.Combine(_captureFolder, $"wes-capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
        bitmap.Save(filePath, ImageFormat.Png);

        return filePath;
    }

    public string ToggleGameBarRecording()
    {
        // Win + Alt + R toggles Xbox Game Bar recording.
        KeyDown(0x5B);
        KeyDown(0x12);
        KeyDown(0x52);
        KeyUp(0x52);
        KeyUp(0x12);
        KeyUp(0x5B);
        return "Toggled Windows Game Bar recording. If Game Bar is enabled, recording should start or stop now.";
    }

    public string OpenCaptureFolder()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_captureFolder}\"",
            UseShellExecute = true
        });

        return _captureFolder;
    }

    private static Rectangle GetPrimaryScreenBounds()
    {
        return new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    private static void KeyDown(byte virtualKey) => keybd_event(virtualKey, 0, 0, 0);

    private static void KeyUp(byte virtualKey) => keybd_event(virtualKey, 0, 0x0002, 0);
}
