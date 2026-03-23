namespace WessTools.Models;

public sealed class UserSettings
{
    public int ThemeIndex { get; set; }

    public bool RefreshOnStartup { get; set; } = true;

    public bool EnableAnimations { get; set; } = true;

    public bool RefreshServicesAfterAction { get; set; } = true;

    public bool EnableLeftAltShortcut { get; set; } = true;

    public bool AutoRefreshRunningApps { get; set; } = true;

    public bool RememberAiHistory { get; set; } = true;

    public bool ShowAppPaths { get; set; } = true;

    public bool SendAiOnEnter { get; set; } = true;

    public bool StartSystemMonitorLive { get; set; } = true;

    public bool OpenCaptureFolderAfterScreenshot { get; set; }

    public bool StartConsoleWhenOpened { get; set; } = true;

    public string InterfaceAnimationMode { get; set; } = "Smooth";

    public string AltAnimationMode { get; set; } = "Pop";

    public string OpenAiApiKey { get; set; } = string.Empty;

    public string OpenAiModel { get; set; } = "gpt-5.1";
}
