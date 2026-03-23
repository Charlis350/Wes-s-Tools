
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WessTools.Models;
using WessTools.Services;

namespace WessTools;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppDiscoveryService _appDiscoveryService = new();
    private readonly RunningAppService _runningAppService = new();
    private readonly WindowsServiceService _windowsServiceService = new();
    private readonly StartupAppService _startupAppService = new();
    private readonly NetworkToolsService _networkToolsService = new();
    private readonly SystemMonitorService _systemMonitorService = new();
    private readonly CaptureService _captureService = new();
    private readonly RepairToolsService _repairToolsService = new();
    private readonly FileToolsService _fileToolsService = new();
    private readonly OptimizationService _optimizationService = new();
    private readonly SettingsService _settingsService = new();
    private readonly OpenAiChatService _openAiChatService = new();
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly List<LaunchableApp> _allApps = [];
    private readonly List<RunningAppEntry> _allRunningApps = [];
    private readonly List<ServiceEntry> _allServices = [];
    private readonly List<StartupAppEntry> _allStartupApps = [];
    private readonly ObservableCollection<LaunchableApp> _visibleApps = [];
    private readonly ObservableCollection<LaunchableApp> _featuredGames = [];
    private readonly ObservableCollection<RunningAppEntry> _visibleRunningApps = [];
    private readonly ObservableCollection<ServiceEntry> _visibleServices = [];
    private readonly ObservableCollection<StartupAppEntry> _visibleStartupApps = [];
    private readonly ObservableCollection<ChatMessage> _chatMessages = [];
    private readonly ObservableCollection<GameProfilePreset> _gameProfiles = [];
    private readonly List<ThemePalette> _themes = CreateThemes();
    private readonly string[] _startupStages = ["Loading assets", "Preparing interface", "Syncing tools", "Opening deck"];
    private readonly string[] _interfaceAnimationModes = ["Smooth", "Snappy", "Minimal", "Float", "Zoom", "Glide", "Orbit"];
    private readonly string[] _altAnimationModes = ["Pop", "Slide", "Fade", "Drift", "Bounce", "Pulse", "Flip"];
    private readonly DispatcherTimer _systemMonitorTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StringBuilder _consoleBuffer = new();
    private UserSettings _settings = new();
    private bool _isApplyingSettings;
    private bool _isSendingAiMessage;
    private string _activePage = "Library";
    private nint _keyboardHookHandle;
    private bool _leftAltDown;
    private bool _otherKeyPressedWhileLeftAlt;
    private bool _isWindowHidden;
    private bool _isVisibilityAnimationRunning;
    private Process? _consoleProcess;
    private bool _isConsoleStarting;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Visibility AppPathVisibility => _settings.ShowAppPaths ? Visibility.Visible : Visibility.Collapsed;

    public MainWindow()
    {
        _keyboardProc = KeyboardHookCallback;
        InitializeComponent();
        DataContext = this;
        AppsListView.ItemsSource = _visibleApps;
        FeaturedGamesItemsControl.ItemsSource = _featuredGames;
        RunningAppsListView.ItemsSource = _visibleRunningApps;
        ServicesListView.ItemsSource = _visibleServices;
        StartupAppsListView.ItemsSource = _visibleStartupApps;
        GameProfilesListView.ItemsSource = _gameProfiles;
        WesAiItemsControl.ItemsSource = _chatMessages;
        _systemMonitorTimer.Tick += (_, _) => RefreshSystemMonitor();
        Loaded += async (_, _) => await InitializeAsync();
        SourceInitialized += (_, _) => InstallKeyboardHook();
        Closed += (_, _) =>
        {
            RemoveKeyboardHook();
            StopConsoleProcess();
        };
    }

    private async Task InitializeAsync()
    {
        TrySetWindowIcon();
        _settings = _settingsService.Load();
        NormalizeSettings();
        ApplySettingsToUi();
        ApplyTheme();
        InitializeGameProfiles();
        RefreshNetworkSummary();
        RefreshSystemMonitor();
        ResetWesAiConversation();
        ShowPage("Library", false);

        Task refreshTask = Task.CompletedTask;
        if (_settings.RefreshOnStartup)
        {
            refreshTask = RefreshAllAsync();
        }
        else
        {
            RefreshStats();
            SetStatus("Ready. Use Refresh Everything when you want a fresh scan.");
        }

        await PlayStartupSequenceAsync(refreshTask);

        if (_settings.EnableAnimations)
        {
            AnimateEntrance();
        }
        else
        {
            SidebarPanel.Opacity = 1;
            HeroPanel.Opacity = 1;
        }
    }

    private async Task RefreshAllAsync()
    {
        SetStatus("Refreshing library, live apps, services, and startup tools...");

        try
        {
            var appsTask = Task.Run(() => _appDiscoveryService.Scan());
            var runningAppsTask = Task.Run(() => _runningAppService.LoadRunningApps());
            var servicesTask = Task.Run(() => _windowsServiceService.LoadServices());
            var startupAppsTask = Task.Run(() => _startupAppService.LoadStartupApps());
            await Task.WhenAll(appsTask, runningAppsTask, servicesTask, startupAppsTask);

            _allApps.Clear();
            _allApps.AddRange(appsTask.Result);
            _allRunningApps.Clear();
            _allRunningApps.AddRange(runningAppsTask.Result);
            _allServices.Clear();
            _allServices.AddRange(servicesTask.Result);

            _allStartupApps.Clear();
            _allStartupApps.AddRange(startupAppsTask.Result);

            ApplyAppFilter();
            ApplyRunningAppsFilter();
            ApplyServiceFilter();
            ApplyStartupAppFilter();
            RefreshStats();
            SetStatus("Everything is up to date.");
        }
        catch (Exception ex)
        {
            SetStatus("Refresh finished with a warning.");
            MessageBox.Show(ex.Message, "Refresh warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshAppsAsync()
    {
        SetStatus("Refreshing verified apps...");
        try
        {
            var apps = await Task.Run(() => _appDiscoveryService.Scan());
            _allApps.Clear();
            _allApps.AddRange(apps);
            ApplyAppFilter();
            RefreshStats();
            SetStatus("Library refreshed.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Library refresh failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshRunningAppsAsync()
    {
        SetStatus("Refreshing running apps...");
        try
        {
            var runningApps = await Task.Run(() => _runningAppService.LoadRunningApps());
            _allRunningApps.Clear();
            _allRunningApps.AddRange(runningApps);
            ApplyRunningAppsFilter();
            RefreshStats();
            SetStatus("Running apps refreshed.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Running apps refresh failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshServicesAsync()
    {
        SetStatus("Refreshing Windows services...");
        try
        {
            var services = await Task.Run(() => _windowsServiceService.LoadServices());
            _allServices.Clear();
            _allServices.AddRange(services);
            ApplyServiceFilter();
            RefreshStats();
            SetStatus("Services refreshed.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Services refresh failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshStartupAppsAsync()
    {
        SetStatus("Refreshing startup apps...");
        try
        {
            var startupApps = await Task.Run(() => _startupAppService.LoadStartupApps());
            _allStartupApps.Clear();
            _allStartupApps.AddRange(startupApps);
            ApplyStartupAppFilter();
            SetStatus("Startup apps refreshed.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Startup apps refresh failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InitializeGameProfiles()
    {
        if (_gameProfiles.Count > 0)
        {
            return;
        }

        _gameProfiles.Add(new GameProfilePreset
        {
            Name = "Competitive Mode",
            Description = "High performance plan plus a DNS flush before launch for fast match sessions.",
            EnableHighPerformance = true,
            FlushDns = true
        });
        _gameProfiles.Add(new GameProfilePreset
        {
            Name = "Clean Boot Mode",
            Description = "High performance, DNS refresh, and temp cleanup before starting a heavier game.",
            EnableHighPerformance = true,
            FlushDns = true,
            CleanTempFiles = true
        });
        _gameProfiles.Add(new GameProfilePreset
        {
            Name = "Quick Launch Mode",
            Description = "Minimal prep so you can launch right away while still having one-click control.",
            EnableHighPerformance = true
        });
    }

    private void RefreshNetworkSummary()
    {
        try
        {
            NetworkSummaryTextBlock.Text = _networkToolsService.GetNetworkSummary();
            if (string.IsNullOrWhiteSpace(NetworkResultTextBlock.Text))
            {
                NetworkResultTextBlock.Text = "Run a ping or refresh the network snapshot to see live results here.";
            }
        }
        catch (Exception ex)
        {
            NetworkSummaryTextBlock.Text = ex.Message;
        }
    }

    private void RefreshSystemMonitor()
    {
        try
        {
            var snapshot = _systemMonitorService.ReadSnapshot();
            CpuUsageTextBlock.Text = $"{snapshot.CpuPercent:0.0}%";
            MemoryUsageTextBlock.Text = $"{snapshot.MemoryPercent:0.0}%";
            MemoryDetailTextBlock.Text = $"{snapshot.UsedMemoryGb:0.0} GB / {snapshot.TotalMemoryGb:0.0} GB in use";
            DownloadSpeedTextBlock.Text = $"Down {snapshot.DownloadMbps:0.00} Mbps";
            UploadSpeedTextBlock.Text = $"Up {snapshot.UploadMbps:0.00} Mbps";
            ProcessCountTextBlock.Text = snapshot.ProcessCount.ToString();
        }
        catch (Exception ex)
        {
            CpuUsageTextBlock.Text = "--";
            MemoryUsageTextBlock.Text = "--";
            MemoryDetailTextBlock.Text = ex.Message;
            DownloadSpeedTextBlock.Text = "--";
            UploadSpeedTextBlock.Text = "--";
            ProcessCountTextBlock.Text = "--";
        }
    }

    private void ApplyAppFilter()
    {
        var term = AppSearchTextBox.Text.Trim();
        var filtered = _allApps
            .Where(app => string.IsNullOrWhiteSpace(term) ||
                          app.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          app.Publisher.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          app.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(app => app.IsGame)
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceItems(_visibleApps, filtered);
        ReplaceItems(_featuredGames, filtered.Where(app => app.IsGame).Take(4));
    }

    private void ApplyRunningAppsFilter()
    {
        var term = RunningAppsSearchTextBox.Text.Trim();
        var filtered = _allRunningApps
            .Where(app => string.IsNullOrWhiteSpace(term) ||
                          app.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          app.WindowTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          app.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(app => app.MemoryText)
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceItems(_visibleRunningApps, filtered);
    }

    private void ApplyServiceFilter()
    {
        var term = ServiceSearchTextBox.Text.Trim();
        var filtered = _allServices
            .Where(service => string.IsNullOrWhiteSpace(term) ||
                              service.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              service.ServiceName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              service.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceItems(_visibleServices, filtered);
    }

    private void ApplyStartupAppFilter()
    {
        var term = StartupSearchTextBox.Text.Trim();
        var filtered = _allStartupApps
            .Where(app => string.IsNullOrWhiteSpace(term) ||
                          app.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          app.SourceLabel.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          app.Command.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceItems(_visibleStartupApps, filtered);
    }

    private void ShowPage(string pageName, bool focus = true)
    {
        _activePage = pageName;
        LibraryView.Visibility = pageName == "Library" ? Visibility.Visible : Visibility.Collapsed;
        RunningAppsView.Visibility = pageName == "RunningApps" ? Visibility.Visible : Visibility.Collapsed;
        OptimizationView.Visibility = pageName == "Optimization" ? Visibility.Visible : Visibility.Collapsed;
        ServicesView.Visibility = pageName == "Services" ? Visibility.Visible : Visibility.Collapsed;
        StartupAppsView.Visibility = pageName == "StartupApps" ? Visibility.Visible : Visibility.Collapsed;
        NetworkToolsView.Visibility = pageName == "NetworkTools" ? Visibility.Visible : Visibility.Collapsed;
        GameProfilesView.Visibility = pageName == "GameProfiles" ? Visibility.Visible : Visibility.Collapsed;
        SystemMonitorView.Visibility = pageName == "SystemMonitor" ? Visibility.Visible : Visibility.Collapsed;
        CaptureHubView.Visibility = pageName == "CaptureHub" ? Visibility.Visible : Visibility.Collapsed;
        ConsoleView.Visibility = pageName == "Console" ? Visibility.Visible : Visibility.Collapsed;
        RepairToolsView.Visibility = pageName == "RepairTools" ? Visibility.Visible : Visibility.Collapsed;
        FileToolsView.Visibility = pageName == "FileTools" ? Visibility.Visible : Visibility.Collapsed;
        WesAiView.Visibility = pageName == "WesAi" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = pageName == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (pageName == "SystemMonitor" && _settings.StartSystemMonitorLive)
        {
            RefreshSystemMonitor();
            _systemMonitorTimer.Start();
        }
        else
        {
            _systemMonitorTimer.Stop();
        }
        switch (pageName)
        {
            case "Library":
                HeroTitleTextBlock.Text = "Your Launcher Library";
                HeroSubtitleTextBlock.Text = "Browse verified apps, see icons when Windows exposes them, and launch tools or games from one clean list.";
                HeroPrimaryButton.Content = "Refresh Library";
                HeroSecondaryButton.Content = "Open Running Apps";
                break;
            case "RunningApps":
                HeroTitleTextBlock.Text = "Running Apps";
                HeroSubtitleTextBlock.Text = "See what is open on your PC, how much memory it is using, and close apps from the deck when you want to free resources.";
                HeroPrimaryButton.Content = "Refresh Running Apps";
                HeroSecondaryButton.Content = "Open Optimization";
                if (_settings.AutoRefreshRunningApps)
                {
                    _ = RefreshRunningAppsAsync();
                }
                break;
            case "Optimization":
                HeroTitleTextBlock.Text = "Optimization";
                HeroSubtitleTextBlock.Text = "Use grounded Windows tweaks that can help sessions feel cleaner without promising impossible FPS or ping results.";
                HeroPrimaryButton.Content = "Flush DNS Cache";
                HeroSecondaryButton.Content = "Open Services";
                break;
            case "Services":
                HeroTitleTextBlock.Text = "Windows Services";
                HeroSubtitleTextBlock.Text = "Search services quickly and start, stop, or restart them where Windows permissions allow it.";
                HeroPrimaryButton.Content = "Refresh Services";
                HeroSecondaryButton.Content = "Open Startup Apps";
                break;
            case "StartupApps":
                HeroTitleTextBlock.Text = "Startup Apps";
                HeroSubtitleTextBlock.Text = "See what launches with Windows, search startup entries fast, and jump straight to the target app or folder.";
                HeroPrimaryButton.Content = "Refresh Startup Apps";
                HeroSecondaryButton.Content = "Open Network Tools";
                break;
            case "NetworkTools":
                HeroTitleTextBlock.Text = "Network Tools";
                HeroSubtitleTextBlock.Text = "Check active adapters, ping hosts, and run quick connection cleanup without leaving Wes's Tools.";
                HeroPrimaryButton.Content = "Refresh Network";
                HeroSecondaryButton.Content = "Open Game Profiles";
                break;
            case "GameProfiles":
                HeroTitleTextBlock.Text = "Game Profiles";
                HeroSubtitleTextBlock.Text = "Apply a small preset of useful Windows gaming actions, then launch a game from the same profile row.";
                HeroPrimaryButton.Content = "Refresh Library";
                HeroSecondaryButton.Content = "Open System Monitor";
                break;
            case "SystemMonitor":
                HeroTitleTextBlock.Text = "System Monitor";
                HeroSubtitleTextBlock.Text = "Watch CPU, memory, process count, and network throughput update live in the app.";
                HeroPrimaryButton.Content = "Refresh Monitor";
                HeroSecondaryButton.Content = "Open Capture Hub";
                break;
            case "CaptureHub":
                HeroTitleTextBlock.Text = "Capture Hub";
                HeroSubtitleTextBlock.Text = "Take screenshots, trigger Windows recording, and keep your captures in one easy place.";
                HeroPrimaryButton.Content = "Take Screenshot";
                HeroSecondaryButton.Content = "Open Console";
                break;
            case "Console":
                HeroTitleTextBlock.Text = "Console";
                HeroSubtitleTextBlock.Text = "Run command prompt commands inside Wes's Tools and watch the output update live.";
                HeroPrimaryButton.Content = "Clear Console";
                HeroSecondaryButton.Content = "Open Repair Tools";
                if (_settings.StartConsoleWhenOpened)
                {
                    _ = EnsureConsoleStartedAsync();
                }
                break;
            case "RepairTools":
                HeroTitleTextBlock.Text = "Repair Tools";
                HeroSubtitleTextBlock.Text = "Launch trusted Windows repair commands and update screens from one place.";
                HeroPrimaryButton.Content = "Open Windows Update";
                HeroSecondaryButton.Content = "Open File Tools";
                break;
            case "FileTools":
                HeroTitleTextBlock.Text = "File Tools";
                HeroSubtitleTextBlock.Text = "Open common folders quickly and generate SHA-256 hashes for files when you need verification.";
                HeroPrimaryButton.Content = "Hash A File";
                HeroSecondaryButton.Content = "Open Wes AI";
                break;
            case "WesAi":
                HeroTitleTextBlock.Text = "Wes AI";
                HeroSubtitleTextBlock.Text = "Chat with Wes inside the app using your OpenAI key. Wes can help with questions, but this in-app assistant cannot build software or generate code.";
                HeroPrimaryButton.Content = "Clear Chat";
                HeroSecondaryButton.Content = "Open Settings";
                break;
            default:
                HeroTitleTextBlock.Text = "Settings";
                HeroSubtitleTextBlock.Text = "Cycle themes, tune behavior, and configure Wes AI the way you want it.";
                HeroPrimaryButton.Content = "Refresh Everything";
                HeroSecondaryButton.Content = "Back To Library";
                break;
        }

        HighlightTabs();
        AnimateCurrentPage();
        if (focus)
        {
            FocusActiveInput();
        }
    }

    private void RefreshStats()
    {
        AppCountTextBlock.Text = $"{_visibleApps.Count} apps visible";
        RunningAppCountTextBlock.Text = $"{_visibleRunningApps.Count} apps visible";
        ServiceCountTextBlock.Text = $"{_visibleServices.Count} services visible";
        HeroAppsTextBlock.Text = _allApps.Count.ToString();
        HeroGamesTextBlock.Text = _allApps.Count(app => app.IsGame).ToString();
        HeroRunningAppsTextBlock.Text = _allRunningApps.Count.ToString();
        HeroServicesTextBlock.Text = _allServices.Count.ToString();
    }

    private void HighlightTabs()
    {
        var accent = (Brush)FindResource("AccentBrush");
        var alt = (Brush)FindResource("PanelAltBrush");
        var text = (Brush)FindResource("TextPrimaryBrush");

        ResetTabButton(LibraryTabButton, alt, text);
        ResetTabButton(RunningAppsTabButton, alt, text);
        ResetTabButton(OptimizationTabButton, alt, text);
        ResetTabButton(ServicesTabButton, alt, text);
        ResetTabButton(StartupAppsTabButton, alt, text);
        ResetTabButton(NetworkToolsTabButton, alt, text);
        ResetTabButton(GameProfilesTabButton, alt, text);
        ResetTabButton(SystemMonitorTabButton, alt, text);
        ResetTabButton(CaptureHubTabButton, alt, text);
        ResetTabButton(ConsoleTabButton, alt, text);
        ResetTabButton(RepairToolsTabButton, alt, text);
        ResetTabButton(FileToolsTabButton, alt, text);
        ResetTabButton(WesAiTabButton, alt, text);
        ResetTabButton(SettingsTabButton, alt, text);

        switch (_activePage)
        {
            case "Library":
                SetActiveTab(LibraryTabButton, accent);
                break;
            case "RunningApps":
                SetActiveTab(RunningAppsTabButton, accent);
                break;
            case "Optimization":
                SetActiveTab(OptimizationTabButton, accent);
                break;
            case "Services":
                SetActiveTab(ServicesTabButton, accent);
                break;
            case "StartupApps":
                SetActiveTab(StartupAppsTabButton, accent);
                break;
            case "NetworkTools":
                SetActiveTab(NetworkToolsTabButton, accent);
                break;
            case "GameProfiles":
                SetActiveTab(GameProfilesTabButton, accent);
                break;
            case "SystemMonitor":
                SetActiveTab(SystemMonitorTabButton, accent);
                break;
            case "CaptureHub":
                SetActiveTab(CaptureHubTabButton, accent);
                break;
            case "Console":
                SetActiveTab(ConsoleTabButton, accent);
                break;
            case "RepairTools":
                SetActiveTab(RepairToolsTabButton, accent);
                break;
            case "FileTools":
                SetActiveTab(FileToolsTabButton, accent);
                break;
            case "WesAi":
                SetActiveTab(WesAiTabButton, accent);
                break;
            default:
                SetActiveTab(SettingsTabButton, accent);
                break;
        }
    }

    private static void ResetTabButton(Button button, Brush background, Brush foreground)
    {
        button.Background = background;
        button.Foreground = foreground;
    }

    private static void SetActiveTab(Button button, Brush accent)
    {
        button.Background = accent;
        button.Foreground = Brushes.Black;
    }

    private void LaunchApp(LaunchableApp app)
    {
        if (!LaunchSafetyService.IsSafe(app.ExecutablePath, out var reason))
        {
            MessageBox.Show(reason, "Blocked for safety", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = app.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(app.ExecutablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        });

        SetStatus($"Launched {app.Name}.");
    }

    private void LaunchStartupApp(StartupAppEntry app)
    {
        if (!app.HasLaunchTarget)
        {
            MessageBox.Show("This startup item does not have a launchable target file.", "Target not found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!LaunchSafetyService.IsSafe(app.ResolvedPath, out var reason))
        {
            MessageBox.Show(reason, "Blocked for safety", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = app.ResolvedPath,
            WorkingDirectory = Path.GetDirectoryName(app.ResolvedPath) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        });

        SetStatus($"Launched startup app {app.Name}.");
    }

    private void OpenStartupAppFolder(StartupAppEntry app)
    {
        if (!app.HasLaunchTarget)
        {
            MessageBox.Show("This startup item does not point to a local file that can be opened.", "Target not found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{app.ResolvedPath}\"",
            UseShellExecute = true
        });

        SetStatus($"Opened the location for {app.Name}.");
    }

    private async Task PingHostAsync()
    {
        var host = PingHostTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            NetworkResultTextBlock.Text = "Type a host or IP address first.";
            return;
        }

        SetStatus($"Pinging {host}...");
        try
        {
            NetworkResultTextBlock.Text = await Task.Run(() => _networkToolsService.PingHost(host));
            SetStatus("Ping finished.");
        }
        catch (Exception ex)
        {
            NetworkResultTextBlock.Text = ex.Message;
            SetStatus("Ping failed.");
        }
    }

    private async Task ApplyGameProfileAsync(GameProfilePreset profile, bool launchAfterApply)
    {
        SetStatus($"Applying profile {profile.Name}...");
        var results = new List<string>();

        if (profile.EnableHighPerformance)
        {
            results.Add(await Task.Run(_optimizationService.EnableHighPerformancePowerPlan));
        }

        if (profile.FlushDns)
        {
            results.Add(await Task.Run(_optimizationService.FlushDnsCache));
        }

        if (profile.CleanTempFiles)
        {
            results.Add(await Task.Run(_optimizationService.CleanTempFiles));
        }

        if (launchAfterApply && profile.TargetApp is not null)
        {
            LaunchApp(profile.TargetApp);
            results.Add($"Launched {profile.TargetApp.Name}.");
        }

        NetworkResultTextBlock.Text = string.Join(" ", results);
        SetStatus($"{profile.Name} is ready.");
    }

    private async Task TakeScreenshotAsync()
    {
        try
        {
            SetStatus("Taking screenshot...");
            var filePath = await Task.Run(_captureService.TakeScreenshot);
            CaptureResultTextBlock.Text = $"Saved screenshot to {filePath}";
            if (_settings.OpenCaptureFolderAfterScreenshot)
            {
                _captureService.OpenCaptureFolder();
            }
            SetStatus("Screenshot saved.");
        }
        catch (Exception ex)
        {
            CaptureResultTextBlock.Text = ex.Message;
            SetStatus("Screenshot failed.");
        }
    }

    private async Task HashSelectedFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a file to hash"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SetStatus("Hashing file...");
            var hash = await Task.Run(() => _fileToolsService.ComputeSha256(dialog.FileName));
            FileToolsResultTextBlock.Text = $"{dialog.FileName}\r\n\r\nSHA-256:\r\n{hash}";
            SetStatus("File hash ready.");
        }
        catch (Exception ex)
        {
            FileToolsResultTextBlock.Text = ex.Message;
            SetStatus("File hash failed.");
        }
    }

    private async Task EnsureConsoleStartedAsync()
    {
        if (_consoleProcess is { HasExited: false } || _isConsoleStarting)
        {
            return;
        }

        _isConsoleStarting = true;
        try
        {
            StopConsoleProcess();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/Q /K",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.CurrentDirectory
                },
                EnableRaisingEvents = true
            };

            process.Exited += (_, _) => Dispatcher.Invoke(() => AppendConsoleOutput("\r\n[Console exited]\r\n"));
            process.Start();
            _consoleProcess = process;

            AppendConsoleOutput($"Microsoft Windows Command Processor\r\nWorking directory: {Environment.CurrentDirectory}\r\n\r\n");
            _ = Task.Run(() => ReadConsoleStreamAsync(process.StandardOutput));
            _ = Task.Run(() => ReadConsoleStreamAsync(process.StandardError));

            await process.StandardInput.WriteLineAsync($"cd /d \"{Environment.CurrentDirectory}\"");
            await process.StandardInput.FlushAsync();
            SetStatus("Console started.");
        }
        catch (Exception ex)
        {
            AppendConsoleOutput($"\r\n[Console failed to start: {ex.Message}]\r\n");
            SetStatus("Console failed to start.");
        }
        finally
        {
            _isConsoleStarting = false;
        }
    }

    private async Task ReadConsoleStreamAsync(StreamReader reader)
    {
        var buffer = new char[512];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            var chunk = new string(buffer, 0, read);
            Dispatcher.Invoke(() => AppendConsoleOutput(chunk));
        }
    }

    private void AppendConsoleOutput(string text)
    {
        _consoleBuffer.Append(text);
        ConsoleOutputTextBox.Text = _consoleBuffer.ToString();
        ConsoleOutputTextBox.CaretIndex = ConsoleOutputTextBox.Text.Length;
        ConsoleOutputTextBox.ScrollToEnd();
    }

    private async Task RunConsoleCommandAsync()
    {
        var command = ConsoleInputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        await EnsureConsoleStartedAsync();
        if (_consoleProcess is null || _consoleProcess.HasExited)
        {
            return;
        }

        AppendConsoleOutput($"\r\n> {command}\r\n");
        ConsoleInputTextBox.Clear();
        await _consoleProcess.StandardInput.WriteLineAsync(command);
        await _consoleProcess.StandardInput.FlushAsync();
        SetStatus($"Ran console command: {command}");
    }

    private void StopConsoleProcess()
    {
        try
        {
            if (_consoleProcess is { HasExited: false })
            {
                _consoleProcess.Kill(true);
                _consoleProcess.Dispose();
            }
        }
        catch
        {
        }
        finally
        {
            _consoleProcess = null;
        }
    }

    private async Task CloseRunningAppAsync(RunningAppEntry app)
    {
        try
        {
            await Task.Run(() => _runningAppService.CloseApp(app));
            SetStatus($"Sent close request to {app.Name}.");
            await RefreshRunningAppsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not close app", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task RunOptimizationAsync(Func<string> action)
    {
        try
        {
            SetStatus("Running optimization...");
            var result = await Task.Run(action);
            SetStatus(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Optimization action failed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ChangeServiceStateAsync(ServiceEntry service, string action)
    {
        try
        {
            switch (action)
            {
                case "Start":
                    await Task.Run(() => _windowsServiceService.StartService(service.ServiceName));
                    break;
                case "Stop":
                    await Task.Run(() => _windowsServiceService.StopService(service.ServiceName));
                    break;
                default:
                    await Task.Run(() => _windowsServiceService.RestartService(service.ServiceName));
                    break;
            }

            SetStatus($"{action}ed {service.DisplayName}.");
            if (_settings.RefreshServicesAfterAction)
            {
                await RefreshServicesAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{ex.Message}\n\nSome services need Administrator rights.", "Service action failed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task SendWesAiMessageAsync()
    {
        if (_isSendingAiMessage)
        {
            return;
        }
        var prompt = WesAiInputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey))
        {
            MessageBox.Show("Add your OpenAI API key in Settings before using Wes AI.", "OpenAI key required", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowPage("Settings");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.OpenAiModel))
        {
            MessageBox.Show("Add an OpenAI model name in Settings before using Wes AI.", "OpenAI model required", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowPage("Settings");
            return;
        }

        _isSendingAiMessage = true;
        SendWesAiButton.IsEnabled = false;
        ClearWesAiButton.IsEnabled = false;

        try
        {
            var userMessage = new ChatMessage { Role = "user", DisplayName = "You", Content = prompt };
            _chatMessages.Add(userMessage);
            WesAiInputTextBox.Clear();
            ScrollWesAiToEnd();
            SetStatus("Waiting for Wes AI...");

            var history = _settings.RememberAiHistory
                ? _chatMessages.Take(Math.Max(0, _chatMessages.Count - 1)).ToList()
                : [];

            var response = await _openAiChatService.SendMessageAsync(
                _settings.OpenAiApiKey,
                _settings.OpenAiModel,
                history,
                prompt,
                _settings.RememberAiHistory,
                CancellationToken.None);

            _chatMessages.Add(new ChatMessage { Role = "assistant", DisplayName = "Wes", Content = response });
            ScrollWesAiToEnd();
            SetStatus("Wes AI replied.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Wes AI request failed", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("Wes AI request failed.");
        }
        finally
        {
            _isSendingAiMessage = false;
            SendWesAiButton.IsEnabled = true;
            ClearWesAiButton.IsEnabled = true;
        }
    }

    private void ResetWesAiConversation()
    {
        _chatMessages.Clear();
        _chatMessages.Add(new ChatMessage
        {
            Role = "assistant",
            DisplayName = "Wes",
            Content = "I am Wes. I can help with Windows apps, settings, services, and general questions. Inside this app I cannot build software, write code, or generate installers."
        });
        ScrollWesAiToEnd();
    }

    private void ApplySettingsToUi()
    {
        _isApplyingSettings = true;
        RefreshOnStartupCheckBox.IsChecked = _settings.RefreshOnStartup;
        EnableAnimationsCheckBox.IsChecked = _settings.EnableAnimations;
        RefreshServicesAfterActionCheckBox.IsChecked = _settings.RefreshServicesAfterAction;
        EnableLeftAltShortcutCheckBox.IsChecked = _settings.EnableLeftAltShortcut;
        AutoRefreshRunningAppsCheckBox.IsChecked = _settings.AutoRefreshRunningApps;
        RememberAiHistoryCheckBox.IsChecked = _settings.RememberAiHistory;
        ShowAppPathsCheckBox.IsChecked = _settings.ShowAppPaths;
        SendAiOnEnterCheckBox.IsChecked = _settings.SendAiOnEnter;
        StartSystemMonitorLiveCheckBox.IsChecked = _settings.StartSystemMonitorLive;
        OpenCaptureFolderAfterScreenshotCheckBox.IsChecked = _settings.OpenCaptureFolderAfterScreenshot;
        StartConsoleWhenOpenedCheckBox.IsChecked = _settings.StartConsoleWhenOpened;
        OpenAiApiKeyPasswordBox.Password = _settings.OpenAiApiKey;
        OpenAiModelTextBox.Text = _settings.OpenAiModel;
        ThemeNameTextBlock.Text = _themes[_settings.ThemeIndex].Name;
        InterfaceAnimationModeTextBlock.Text = _settings.InterfaceAnimationMode;
        AltAnimationModeTextBlock.Text = _settings.AltAnimationMode;
        _isApplyingSettings = false;
    }

    private void ApplyTheme()
    {
        var theme = _themes[_settings.ThemeIndex];
        Application.Current.Resources["BackgroundBrush"] = new SolidColorBrush(theme.Background);
        Application.Current.Resources["SidebarBrush"] = new SolidColorBrush(theme.Sidebar);
        Application.Current.Resources["PanelBrush"] = new SolidColorBrush(theme.Panel);
        Application.Current.Resources["PanelAltBrush"] = new SolidColorBrush(theme.PanelAlt);
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(theme.Accent);
        Application.Current.Resources["AccentAltBrush"] = new SolidColorBrush(theme.AccentAlt);
        Application.Current.Resources["TextPrimaryBrush"] = new SolidColorBrush(theme.TextPrimary);
        Application.Current.Resources["TextSecondaryBrush"] = new SolidColorBrush(theme.TextSecondary);
        Application.Current.Resources["ScrollTrackBrush"] = new SolidColorBrush(Color.FromArgb(0x58, theme.Sidebar.R, theme.Sidebar.G, theme.Sidebar.B));
        Application.Current.Resources["ScrollThumbBrush"] = new SolidColorBrush(Color.FromArgb(0xAA, theme.Accent.R, theme.Accent.G, theme.Accent.B));
        Application.Current.Resources["ScrollThumbHoverBrush"] = new SolidColorBrush(Color.FromArgb(0xEE, theme.AccentAlt.R, theme.AccentAlt.G, theme.AccentAlt.B));
        Application.Current.Resources["HeroBrush"] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(theme.HeroStart, 0),
                new GradientStop(theme.HeroMiddle, 0.45),
                new GradientStop(theme.HeroEnd, 1)
            }
        };

        ThemeNameTextBlock.Text = theme.Name;
        HighlightTabs();
    }

    private void NormalizeSettings()
    {
        _settings.ThemeIndex = _themes.Count == 0
            ? 0
            : ((_settings.ThemeIndex % _themes.Count) + _themes.Count) % _themes.Count;

        if (!_interfaceAnimationModes.Contains(_settings.InterfaceAnimationMode, StringComparer.OrdinalIgnoreCase))
        {
            _settings.InterfaceAnimationMode = _interfaceAnimationModes[0];
        }

        if (!_altAnimationModes.Contains(_settings.AltAnimationMode, StringComparer.OrdinalIgnoreCase))
        {
            _settings.AltAnimationMode = _altAnimationModes[0];
        }
    }

    private void SaveSettings()
    {
        NormalizeSettings();
        _settingsService.Save(_settings);
    }

    private void CycleInterfaceAnimation(int direction)
    {
        var currentIndex = Array.FindIndex(_interfaceAnimationModes, mode => string.Equals(mode, _settings.InterfaceAnimationMode, StringComparison.OrdinalIgnoreCase));
        currentIndex = currentIndex < 0 ? 0 : currentIndex;
        _settings.InterfaceAnimationMode = _interfaceAnimationModes[(currentIndex + direction + _interfaceAnimationModes.Length) % _interfaceAnimationModes.Length];
        InterfaceAnimationModeTextBlock.Text = _settings.InterfaceAnimationMode;
        SaveSettings();
    }

    private void CycleAltAnimation(int direction)
    {
        var currentIndex = Array.FindIndex(_altAnimationModes, mode => string.Equals(mode, _settings.AltAnimationMode, StringComparison.OrdinalIgnoreCase));
        currentIndex = currentIndex < 0 ? 0 : currentIndex;
        _settings.AltAnimationMode = _altAnimationModes[(currentIndex + direction + _altAnimationModes.Length) % _altAnimationModes.Length];
        AltAnimationModeTextBlock.Text = _settings.AltAnimationMode;
        SaveSettings();
    }

    private void AnimateEntrance()
    {
        SidebarPanel.Opacity = 0;
        HeroPanel.Opacity = 0;
        SidebarPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        HeroPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));

        if (SidebarPanel.RenderTransform is TranslateTransform sidebarTransform)
        {
            sidebarTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-30, 0, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        if (HeroPanel.RenderTransform is TranslateTransform heroTransform)
        {
            heroTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(430))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private async Task PlayStartupSequenceAsync(Task refreshTask)
    {
        StartupOverlay.Visibility = Visibility.Visible;
        StartupOverlay.Opacity = 1;
        RootShell.Opacity = 0;
        StartupStageTextBlock.Text = _startupStages[0];
        StartupPercentTextBlock.Text = "0%";

        if (StartupProgressBar.RenderTransform is not ScaleTransform progressTransform)
        {
            progressTransform = new ScaleTransform(0, 1);
            StartupProgressBar.RenderTransform = progressTransform;
        }

        AnimateStartupSlideIn();

        if (StartupLogoImage.RenderTransform is TransformGroup logoGroup &&
            logoGroup.Children.OfType<ScaleTransform>().FirstOrDefault() is { } logoScale &&
            logoGroup.Children.OfType<TranslateTransform>().FirstOrDefault() is { } logoOffset)
        {
            var scaleAnimation = new DoubleAnimation(0.86, 1.02, TimeSpan.FromMilliseconds(650))
            {
                BeginTime = TimeSpan.FromMilliseconds(160),
                EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
            };
            logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

            logoOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(650))
            {
                BeginTime = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            var pulseAnimation = new DoubleAnimation(1.0, 1.05, TimeSpan.FromMilliseconds(980))
            {
                BeginTime = TimeSpan.FromMilliseconds(820),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
        }

        var checkpoints = new[] { 0.18, 0.46, 0.78, 1.0 };
        for (var index = 0; index < _startupStages.Length; index++)
        {
            StartupStageTextBlock.Text = index == _startupStages.Length - 1 && !refreshTask.IsCompleted
                ? "Opening deck while the rest syncs in the background"
                : _startupStages[index];
            StartupPercentTextBlock.Text = $"{(int)Math.Round(checkpoints[index] * 100)}%";
            progressTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(checkpoints[index], TimeSpan.FromMilliseconds(760))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            await Task.Delay(index == _startupStages.Length - 1 ? 620 : 720);
        }

        await Task.Delay(260);

        RootShell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        var fadeAnimation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        fadeAnimation.Completed += (_, _) => StartupOverlay.Visibility = Visibility.Collapsed;
        StartupOverlay.BeginAnimation(OpacityProperty, fadeAnimation);
    }

    private void AnimateStartupSlideIn()
    {
        AnimateStartupElement(StartupCard, 38, 0, 0, 1, 0);
        AnimateStartupElement(StartupHeaderPanel, 26, 0, 0, 1, 80);
        AnimateStartupElement(StartupLoadingPanel, 24, 0, 0.15, 1, 180);
        AnimateStartupElement(StartupProgressPanel, 24, 0, 0.15, 1, 260);
        AnimateStartupElement(StartupCreditsPanel, 28, 0, 0.15, 1, 340);

        StartupTitleTextBlock.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(500))
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        StartupSubtitleTextBlock.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(540))
        {
            BeginTime = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void AnimateStartupElement(FrameworkElement element, double fromY, double toY, double fromOpacity, double toOpacity, int delayMs)
    {
        element.Opacity = fromOpacity;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(fromOpacity, toOpacity, TimeSpan.FromMilliseconds(620))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        switch (element.RenderTransform)
        {
            case TranslateTransform translate:
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(620))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                });
                break;
            case TransformGroup group when group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } groupTranslate:
                groupTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(620))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                });
                if (group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } groupScale)
                {
                    groupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(620))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(delayMs),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                    groupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(620))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(delayMs),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                break;
        }
    }

    private void AnimateCurrentPage()
    {
        if (!_settings.EnableAnimations)
        {
            return;
        }

        var activeView = GetActiveView();
        if (activeView is null)
        {
            return;
        }

        switch (_settings.InterfaceAnimationMode)
        {
            case "Minimal":
                activeView.Opacity = 1;
                activeView.RenderTransform = Transform.Identity;
                break;
            case "Orbit":
                activeView.Opacity = 0;
                activeView.RenderTransformOrigin = new Point(0.5, 0.5);
                activeView.RenderTransform = new RotateTransform(-2.5);
                activeView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                if (activeView.RenderTransform is RotateTransform orbitTransform)
                {
                    orbitTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-2.5, 0, TimeSpan.FromMilliseconds(280))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                break;
            case "Glide":
                activeView.Opacity = 0;
                activeView.RenderTransform = new TranslateTransform(-38, 0);
                activeView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                if (activeView.RenderTransform is TranslateTransform glideTransform)
                {
                    glideTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-38, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                break;
            case "Snappy":
                activeView.Opacity = 0;
                activeView.RenderTransform = new TranslateTransform(18, 0);
                activeView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
                if (activeView.RenderTransform is TranslateTransform snappyTransform)
                {
                    snappyTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(140)));
                }
                break;
            case "Float":
                activeView.Opacity = 0;
                activeView.RenderTransform = new TranslateTransform(0, 28);
                activeView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                if (activeView.RenderTransform is TranslateTransform floatTransform)
                {
                    floatTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(320))
                    {
                        EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                break;
            case "Zoom":
                activeView.Opacity = 0;
                activeView.RenderTransformOrigin = new Point(0.5, 0.5);
                activeView.RenderTransform = new ScaleTransform(0.96, 0.96);
                activeView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                if (activeView.RenderTransform is ScaleTransform zoomTransform)
                {
                    var zoom = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    zoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
                    zoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
                }
                break;
            default:
                activeView.Opacity = 0;
                activeView.RenderTransform = new TranslateTransform(0, 18);
                activeView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
                if (activeView.RenderTransform is TranslateTransform smoothTransform)
                {
                    smoothTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(240))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
                break;
        }
    }

    private FrameworkElement? GetActiveView()
    {
        return _activePage switch
        {
            "Library" => LibraryView,
            "RunningApps" => RunningAppsView,
            "Optimization" => OptimizationView,
            "Services" => ServicesView,
            "StartupApps" => StartupAppsView,
            "NetworkTools" => NetworkToolsView,
            "GameProfiles" => GameProfilesView,
            "SystemMonitor" => SystemMonitorView,
            "CaptureHub" => CaptureHubView,
            "Console" => ConsoleView,
            "RepairTools" => RepairToolsView,
            "FileTools" => FileToolsView,
            "WesAi" => WesAiView,
            _ => SettingsView
        };
    }

    private void FocusActiveInput()
    {
        switch (_activePage)
        {
            case "Library":
                Keyboard.Focus(AppSearchTextBox);
                break;
            case "RunningApps":
                Keyboard.Focus(RunningAppsSearchTextBox);
                break;
            case "Services":
                Keyboard.Focus(ServiceSearchTextBox);
                break;
            case "StartupApps":
                Keyboard.Focus(StartupSearchTextBox);
                break;
            case "NetworkTools":
                Keyboard.Focus(PingHostTextBox);
                break;
            case "Console":
                Keyboard.Focus(ConsoleInputTextBox);
                break;
            case "FileTools":
                Keyboard.Focus(HashFileButton);
                break;
            case "WesAi":
                Keyboard.Focus(WesAiInputTextBox);
                break;
        }
    }

    private void SetStatus(string message) => StatusTextBlock.Text = message;

    private void ScrollWesAiToEnd()
    {
        Dispatcher.BeginInvoke(() => WesAiScrollViewer.ScrollToEnd(), DispatcherPriority.Background);
    }

    private static void ReplaceItems<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
    private void TrySetWindowIcon()
    {
        try
        {
            var iconPath = ResolveIconPath();
            var iconSource = LoadIconSource(iconPath);
            if (iconSource is not null)
            {
                Icon = iconSource;
                BrandIconImage.Source = iconSource;
                StartupLogoImage.Source = iconSource;
            }
        }
        catch
        {
        }
    }

    private static string ResolveIconPath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WessTools.ico");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var projectPath = Path.Combine(Environment.CurrentDirectory, "Assets", "WessTools.ico");
        return File.Exists(projectPath) ? projectPath : outputPath;
    }

    private static ImageSource? LoadIconSource(string iconPath)
    {
        if (!File.Exists(iconPath))
        {
            return null;
        }

        using var stream = File.OpenRead(iconPath);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames
            .OrderByDescending(candidate => candidate.PixelWidth * candidate.PixelHeight)
            .FirstOrDefault();

        if (frame?.CanFreeze == true)
        {
            frame.Freeze();
        }

        return frame;
    }

    private static List<ThemePalette> CreateThemes()
    {
        return
        [
            new ThemePalette { Name = "Nebula Blue", Background = Color.FromRgb(11,16,32), Sidebar = Color.FromRgb(17,25,46), Panel = Color.FromRgb(21,31,56), PanelAlt = Color.FromRgb(26,39,68), Accent = Color.FromRgb(39,198,248), AccentAlt = Color.FromRgb(255,122,69), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(158,178,213), HeroStart = Color.FromRgb(38,59,114), HeroMiddle = Color.FromRgb(23,36,66), HeroEnd = Color.FromRgb(50,28,56) },
            new ThemePalette { Name = "Carbon Red", Background = Color.FromRgb(18,14,18), Sidebar = Color.FromRgb(31,19,24), Panel = Color.FromRgb(42,24,32), PanelAlt = Color.FromRgb(57,31,40), Accent = Color.FromRgb(255,92,92), AccentAlt = Color.FromRgb(255,186,73), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(214,174,181), HeroStart = Color.FromRgb(96,28,46), HeroMiddle = Color.FromRgb(58,22,31), HeroEnd = Color.FromRgb(30,16,22) },
            new ThemePalette { Name = "Aurora Mint", Background = Color.FromRgb(10,23,23), Sidebar = Color.FromRgb(14,34,34), Panel = Color.FromRgb(18,44,45), PanelAlt = Color.FromRgb(24,57,59), Accent = Color.FromRgb(82,243,196), AccentAlt = Color.FromRgb(255,153,102), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(163,213,207), HeroStart = Color.FromRgb(30,95,86), HeroMiddle = Color.FromRgb(18,53,52), HeroEnd = Color.FromRgb(14,31,39) },
            new ThemePalette { Name = "Sunset Gold", Background = Color.FromRgb(23,16,11), Sidebar = Color.FromRgb(38,25,15), Panel = Color.FromRgb(53,33,19), PanelAlt = Color.FromRgb(73,44,25), Accent = Color.FromRgb(255,191,71), AccentAlt = Color.FromRgb(255,115,92), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(221,194,157), HeroStart = Color.FromRgb(124,72,25), HeroMiddle = Color.FromRgb(77,43,22), HeroEnd = Color.FromRgb(43,25,16) },
            new ThemePalette { Name = "Midnight Ice", Background = Color.FromRgb(7,12,18), Sidebar = Color.FromRgb(12,22,32), Panel = Color.FromRgb(18,29,42), PanelAlt = Color.FromRgb(22,37,54), Accent = Color.FromRgb(125,221,255), AccentAlt = Color.FromRgb(118,255,206), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(161,191,214), HeroStart = Color.FromRgb(22,79,112), HeroMiddle = Color.FromRgb(18,37,61), HeroEnd = Color.FromRgb(18,19,32) },
            new ThemePalette { Name = "Rose Night", Background = Color.FromRgb(19,11,18), Sidebar = Color.FromRgb(29,16,27), Panel = Color.FromRgb(42,21,37), PanelAlt = Color.FromRgb(57,29,49), Accent = Color.FromRgb(255,128,178), AccentAlt = Color.FromRgb(255,214,102), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(221,174,195), HeroStart = Color.FromRgb(119,38,76), HeroMiddle = Color.FromRgb(70,25,48), HeroEnd = Color.FromRgb(33,15,28) },
            new ThemePalette { Name = "Volt Lime", Background = Color.FromRgb(12,18,9), Sidebar = Color.FromRgb(20,29,13), Panel = Color.FromRgb(28,41,18), PanelAlt = Color.FromRgb(38,56,24), Accent = Color.FromRgb(174,255,79), AccentAlt = Color.FromRgb(255,191,87), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(193,220,159), HeroStart = Color.FromRgb(67,110,25), HeroMiddle = Color.FromRgb(37,60,23), HeroEnd = Color.FromRgb(20,28,18) },
            new ThemePalette { Name = "Electric Coral", Background = Color.FromRgb(24,10,16), Sidebar = Color.FromRgb(38,16,25), Panel = Color.FromRgb(54,22,36), PanelAlt = Color.FromRgb(70,29,46), Accent = Color.FromRgb(255,111,97), AccentAlt = Color.FromRgb(255,209,102), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(231,180,175), HeroStart = Color.FromRgb(140,48,61), HeroMiddle = Color.FromRgb(86,30,43), HeroEnd = Color.FromRgb(38,16,23) },
            new ThemePalette { Name = "Arctic White", Background = Color.FromRgb(221,231,241), Sidebar = Color.FromRgb(204,216,230), Panel = Color.FromRgb(240,246,252), PanelAlt = Color.FromRgb(226,236,247), Accent = Color.FromRgb(29,111,184), AccentAlt = Color.FromRgb(38,184,146), TextPrimary = Color.FromRgb(17,28,43), TextSecondary = Color.FromRgb(74,93,119), HeroStart = Color.FromRgb(150,190,225), HeroMiddle = Color.FromRgb(199,219,239), HeroEnd = Color.FromRgb(228,238,248) },
            new ThemePalette { Name = "Desert Copper", Background = Color.FromRgb(36,24,17), Sidebar = Color.FromRgb(53,35,24), Panel = Color.FromRgb(70,45,29), PanelAlt = Color.FromRgb(91,57,36), Accent = Color.FromRgb(212,132,72), AccentAlt = Color.FromRgb(245,211,122), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(216,187,164), HeroStart = Color.FromRgb(142,86,47), HeroMiddle = Color.FromRgb(91,55,35), HeroEnd = Color.FromRgb(48,31,21) },
            new ThemePalette { Name = "Ocean Drive", Background = Color.FromRgb(7,27,36), Sidebar = Color.FromRgb(12,41,54), Panel = Color.FromRgb(15,56,73), PanelAlt = Color.FromRgb(18,73,94), Accent = Color.FromRgb(70,219,214), AccentAlt = Color.FromRgb(255,160,107), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(166,211,215), HeroStart = Color.FromRgb(21,116,132), HeroMiddle = Color.FromRgb(15,68,86), HeroEnd = Color.FromRgb(15,31,42) },
            new ThemePalette { Name = "Royal Plum", Background = Color.FromRgb(24,13,31), Sidebar = Color.FromRgb(34,18,45), Panel = Color.FromRgb(47,24,61), PanelAlt = Color.FromRgb(62,30,80), Accent = Color.FromRgb(202,132,255), AccentAlt = Color.FromRgb(255,184,108), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(206,178,227), HeroStart = Color.FromRgb(108,56,156), HeroMiddle = Color.FromRgb(62,31,95), HeroEnd = Color.FromRgb(30,17,40) },
            new ThemePalette { Name = "Forest Ember", Background = Color.FromRgb(14,21,15), Sidebar = Color.FromRgb(23,34,23), Panel = Color.FromRgb(31,48,31), PanelAlt = Color.FromRgb(40,61,40), Accent = Color.FromRgb(111,222,138), AccentAlt = Color.FromRgb(255,126,85), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(177,209,178), HeroStart = Color.FromRgb(58,113,62), HeroMiddle = Color.FromRgb(36,62,38), HeroEnd = Color.FromRgb(24,30,20) },
            new ThemePalette { Name = "Mono Ink", Background = Color.FromRgb(14,14,16), Sidebar = Color.FromRgb(22,22,27), Panel = Color.FromRgb(30,31,37), PanelAlt = Color.FromRgb(41,42,50), Accent = Color.FromRgb(237,237,237), AccentAlt = Color.FromRgb(125,199,255), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(159,165,180), HeroStart = Color.FromRgb(84,88,105), HeroMiddle = Color.FromRgb(36,39,52), HeroEnd = Color.FromRgb(20,20,23) },
            new ThemePalette { Name = "Candy Pop", Background = Color.FromRgb(29,14,29), Sidebar = Color.FromRgb(42,18,42), Panel = Color.FromRgb(58,24,59), PanelAlt = Color.FromRgb(75,31,77), Accent = Color.FromRgb(255,109,191), AccentAlt = Color.FromRgb(101,222,255), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(231,184,224), HeroStart = Color.FromRgb(150,52,142), HeroMiddle = Color.FromRgb(88,32,88), HeroEnd = Color.FromRgb(43,17,43) },
            new ThemePalette { Name = "Citrus Storm", Background = Color.FromRgb(23,23,10), Sidebar = Color.FromRgb(34,35,14), Panel = Color.FromRgb(48,49,18), PanelAlt = Color.FromRgb(63,66,24), Accent = Color.FromRgb(244,223,91), AccentAlt = Color.FromRgb(100,232,178), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(212,212,162), HeroStart = Color.FromRgb(127,121,37), HeroMiddle = Color.FromRgb(73,71,26), HeroEnd = Color.FromRgb(28,28,16) },
            new ThemePalette { Name = "Steelwave", Background = Color.FromRgb(16,21,28), Sidebar = Color.FromRgb(24,31,40), Panel = Color.FromRgb(33,43,55), PanelAlt = Color.FromRgb(42,54,68), Accent = Color.FromRgb(115,179,255), AccentAlt = Color.FromRgb(255,151,120), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(174,191,211), HeroStart = Color.FromRgb(58,96,148), HeroMiddle = Color.FromRgb(35,52,80), HeroEnd = Color.FromRgb(20,27,35) },
            new ThemePalette { Name = "Lavender Fog", Background = Color.FromRgb(26,22,36), Sidebar = Color.FromRgb(36,31,49), Panel = Color.FromRgb(49,42,66), PanelAlt = Color.FromRgb(63,54,84), Accent = Color.FromRgb(189,170,255), AccentAlt = Color.FromRgb(255,170,214), TextPrimary = Color.FromRgb(243,247,255), TextSecondary = Color.FromRgb(203,196,226), HeroStart = Color.FromRgb(111,94,177), HeroMiddle = Color.FromRgb(67,55,101), HeroEnd = Color.FromRgb(31,26,43) }
        ];
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHookHandle != 0)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _keyboardHookHandle = SetWindowsHookEx(13, _keyboardProc, GetModuleHandle(module?.ModuleName), 0);
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHookHandle != 0)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = 0;
        }
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var vkCode = Marshal.ReadInt32(lParam);

            if (message is 0x100 or 0x104)
            {
                if (vkCode == 0xA4)
                {
                    if (!_leftAltDown)
                    {
                        _leftAltDown = true;
                        _otherKeyPressedWhileLeftAlt = false;
                    }
                }
                else if (_leftAltDown)
                {
                    _otherKeyPressedWhileLeftAlt = true;
                }
            }
            else if (message is 0x101 or 0x105 && vkCode == 0xA4)
            {
                var toggle = _settings.EnableLeftAltShortcut && _leftAltDown && !_otherKeyPressedWhileLeftAlt;
                _leftAltDown = false;
                _otherKeyPressedWhileLeftAlt = false;
                if (toggle)
                {
                    Dispatcher.BeginInvoke(ToggleWindowVisibility);
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
    }

    private void ToggleWindowVisibility()
    {
        if (_isVisibilityAnimationRunning)
        {
            return;
        }

        if (_isWindowHidden)
        {
            AnimateShow();
        }
        else
        {
            AnimateHide();
        }
    }

    private void AnimateShow()
    {
        _isVisibilityAnimationRunning = true;
        Show();
        Activate();
        if (RootShell.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            RootShell.RenderTransform = scale;
        }

        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170));

        fade.Completed += (_, _) =>
        {
            _isWindowHidden = false;
            _isVisibilityAnimationRunning = false;
        };

        BeginAnimation(OpacityProperty, fade);
        switch (_settings.AltAnimationMode)
        {
            case "Slide":
                Left += 28;
                BeginAnimation(LeftProperty, new DoubleAnimation(Left, Left - 28, TimeSpan.FromMilliseconds(180)));
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                break;
            case "Fade":
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                break;
            case "Drift":
                Top += 20;
                BeginAnimation(TopProperty, new DoubleAnimation(Top, Top - 20, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                break;
            case "Pulse":
                scale.ScaleX = 0.86;
                scale.ScaleY = 0.86;
                var pulseGrow = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(230))
                {
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseGrow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseGrow);
                break;
            case "Flip":
                RootShell.RenderTransformOrigin = new Point(0.5, 0.5);
                RootShell.RenderTransform = new ScaleTransform(0.84, 1);
                if (RootShell.RenderTransform is ScaleTransform flipScale)
                {
                    var flipGrow = new DoubleAnimation(0.84, 1, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    flipScale.BeginAnimation(ScaleTransform.ScaleXProperty, flipGrow);
                }
                break;
            case "Bounce":
                scale.ScaleX = 0.9;
                scale.ScaleY = 0.9;
                var bounceGrow = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 2 }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounceGrow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceGrow);
                break;
            default:
                scale.ScaleX = 0.94;
                scale.ScaleY = 0.94;
                var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(190))
                {
                    EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                break;
        }
    }

    private void AnimateHide()
    {
        _isVisibilityAnimationRunning = true;
        if (RootShell.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            RootShell.RenderTransform = scale;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130));

        fade.Completed += (_, _) =>
        {
            Hide();
            Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            _isWindowHidden = true;
            _isVisibilityAnimationRunning = false;
        };

        BeginAnimation(OpacityProperty, fade);
        switch (_settings.AltAnimationMode)
        {
            case "Slide":
                BeginAnimation(LeftProperty, new DoubleAnimation(Left, Left + 28, TimeSpan.FromMilliseconds(150)));
                break;
            case "Fade":
                break;
            case "Drift":
                BeginAnimation(TopProperty, new DoubleAnimation(Top, Top + 20, TimeSpan.FromMilliseconds(170)));
                break;
            case "Pulse":
                var pulseShrink = new DoubleAnimation(1, 0.86, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseShrink);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseShrink);
                break;
            case "Flip":
                RootShell.RenderTransformOrigin = new Point(0.5, 0.5);
                RootShell.RenderTransform = new ScaleTransform(1, 1);
                if (RootShell.RenderTransform is ScaleTransform flipShrink)
                {
                    flipShrink.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.84, TimeSpan.FromMilliseconds(160)));
                }
                break;
            case "Bounce":
                var bounceShrink = new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounceShrink);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceShrink);
                break;
            default:
                var shrink = new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
                break;
        }
    }
    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private async void RefreshAllButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAllAsync();
    private async void RefreshAppsButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAppsAsync();
    private async void RefreshRunningAppsButton_OnClick(object sender, RoutedEventArgs e) => await RefreshRunningAppsAsync();
    private async void RefreshServicesButton_OnClick(object sender, RoutedEventArgs e) => await RefreshServicesAsync();
    private async void RefreshStartupAppsButton_OnClick(object sender, RoutedEventArgs e) => await RefreshStartupAppsAsync();
    private void RefreshNetworkButton_OnClick(object sender, RoutedEventArgs e) => RefreshNetworkSummary();
    private void RefreshSystemMonitorButton_OnClick(object sender, RoutedEventArgs e) => RefreshSystemMonitor();
    private void LibraryTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("Library");
    private void RunningAppsTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("RunningApps");
    private void OptimizationTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("Optimization");
    private void ServicesTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("Services");
    private void StartupAppsTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("StartupApps");
    private void NetworkToolsTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("NetworkTools");
    private void GameProfilesTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("GameProfiles");
    private void SystemMonitorTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("SystemMonitor");
    private void CaptureHubTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("CaptureHub");
    private void ConsoleTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("Console");
    private void RepairToolsTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("RepairTools");
    private void FileToolsTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("FileTools");
    private void WesAiTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("WesAi");
    private void SettingsTabButton_OnClick(object sender, RoutedEventArgs e) => ShowPage("Settings");

    private async void HeroPrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        switch (_activePage)
        {
            case "Library":
                await RefreshAppsAsync();
                break;
            case "RunningApps":
                await RefreshRunningAppsAsync();
                break;
            case "Optimization":
                await RunOptimizationAsync(_optimizationService.FlushDnsCache);
                break;
            case "Services":
                await RefreshServicesAsync();
                break;
            case "StartupApps":
                await RefreshStartupAppsAsync();
                break;
            case "NetworkTools":
                RefreshNetworkSummary();
                break;
            case "GameProfiles":
                await RefreshAppsAsync();
                break;
            case "SystemMonitor":
                RefreshSystemMonitor();
                break;
            case "CaptureHub":
                await TakeScreenshotAsync();
                break;
            case "Console":
                _consoleBuffer.Clear();
                ConsoleOutputTextBox.Clear();
                break;
            case "RepairTools":
                RepairResultTextBlock.Text = _repairToolsService.OpenWindowsUpdate();
                break;
            case "FileTools":
                await HashSelectedFileAsync();
                break;
            case "WesAi":
                ResetWesAiConversation();
                break;
            default:
                await RefreshAllAsync();
                break;
        }
    }

    private void HeroSecondaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        switch (_activePage)
        {
            case "Library":
                ShowPage("RunningApps");
                break;
            case "RunningApps":
                ShowPage("Optimization");
                break;
            case "Optimization":
                ShowPage("Services");
                break;
            case "Services":
                ShowPage("StartupApps");
                break;
            case "StartupApps":
                ShowPage("NetworkTools");
                break;
            case "NetworkTools":
                ShowPage("GameProfiles");
                break;
            case "GameProfiles":
                ShowPage("SystemMonitor");
                break;
            case "SystemMonitor":
                ShowPage("CaptureHub");
                break;
            case "CaptureHub":
                ShowPage("Console");
                break;
            case "Console":
                ShowPage("RepairTools");
                break;
            case "RepairTools":
                ShowPage("FileTools");
                break;
            case "FileTools":
                ShowPage("WesAi");
                break;
            case "WesAi":
                ShowPage("Settings");
                break;
            default:
                ShowPage("Library");
                break;
        }
    }

    private void AppSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => ApplyAppFilter();
    private void RunningAppsSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => ApplyRunningAppsFilter();
    private void ServiceSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => ApplyServiceFilter();
    private void StartupSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => ApplyStartupAppFilter();

    private void LaunchAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LaunchableApp app })
        {
            LaunchApp(app);
        }
    }

    private async void CloseRunningAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RunningAppEntry app })
        {
            await CloseRunningAppAsync(app);
        }
    }

    private async void HighPerformanceButton_OnClick(object sender, RoutedEventArgs e) => await RunOptimizationAsync(_optimizationService.EnableHighPerformancePowerPlan);
    private async void FlushDnsButton_OnClick(object sender, RoutedEventArgs e) => await RunOptimizationAsync(_optimizationService.FlushDnsCache);
    private async void CleanTempButton_OnClick(object sender, RoutedEventArgs e) => await RunOptimizationAsync(_optimizationService.CleanTempFiles);
    private async void GameModeButton_OnClick(object sender, RoutedEventArgs e) => await RunOptimizationAsync(_optimizationService.OpenGameModeSettings);

    private async void StartServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServiceEntry service })
        {
            await ChangeServiceStateAsync(service, "Start");
        }
    }

    private async void StopServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServiceEntry service })
        {
            await ChangeServiceStateAsync(service, "Stop");
        }
    }

    private async void RestartServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ServiceEntry service })
        {
            await ChangeServiceStateAsync(service, "Restart");
        }
    }

    private void LaunchStartupAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StartupAppEntry app })
        {
            LaunchStartupApp(app);
        }
    }

    private void OpenStartupAppFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StartupAppEntry app })
        {
            OpenStartupAppFolder(app);
        }
    }

    private async void PingHostButton_OnClick(object sender, RoutedEventArgs e) => await PingHostAsync();

    private async void ApplyProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameProfilePreset profile })
        {
            await ApplyGameProfileAsync(profile, false);
        }
    }

    private async void LaunchProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameProfilePreset profile })
        {
            await ApplyGameProfileAsync(profile, true);
        }
    }

    private void GameProfileAppComboBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.Tag is GameProfilePreset profile)
        {
            var games = _allApps.Where(app => app.IsGame).OrderBy(app => app.Name).ToList();
            comboBox.ItemsSource = games;
            comboBox.DisplayMemberPath = nameof(LaunchableApp.Name);
            comboBox.SelectedItem = games.FirstOrDefault(app => string.Equals(app.ExecutablePath, profile.TargetApp?.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void GameProfileAppComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { Tag: GameProfilePreset profile, SelectedItem: LaunchableApp selectedApp })
        {
            profile.TargetApp = selectedApp;
        }
    }

    private async void TakeScreenshotButton_OnClick(object sender, RoutedEventArgs e) => await TakeScreenshotAsync();

    private async void ToggleRecordingButton_OnClick(object sender, RoutedEventArgs e)
    {
        var result = await Task.Run(_captureService.ToggleGameBarRecording);
        CaptureResultTextBlock.Text = result;
        SetStatus(result);
    }

    private void OpenCaptureFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var folder = _captureService.OpenCaptureFolder();
        CaptureResultTextBlock.Text = $"Opened {folder}";
        SetStatus("Capture folder opened.");
    }

    private async void RunConsoleCommandButton_OnClick(object sender, RoutedEventArgs e) => await RunConsoleCommandAsync();

    private async void ConsoleInputTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunConsoleCommandAsync();
        }
    }

    private async void RestartConsoleButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopConsoleProcess();
        _consoleBuffer.Clear();
        ConsoleOutputTextBox.Clear();
        await EnsureConsoleStartedAsync();
    }

    private void ClearConsoleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _consoleBuffer.Clear();
        ConsoleOutputTextBox.Clear();
        SetStatus("Console output cleared.");
    }

    private void SidebarTabsScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SidebarTabsScrollViewer.ScrollToVerticalOffset(Math.Max(0, SidebarTabsScrollViewer.VerticalOffset - e.Delta / 6.0));
        e.Handled = true;
    }

    private void RunSfcButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RepairResultTextBlock.Text = _repairToolsService.RunSystemFileChecker();
            SetStatus("Started SFC.");
        }
        catch (Exception ex)
        {
            RepairResultTextBlock.Text = ex.Message;
            SetStatus("Could not start SFC.");
        }
    }

    private void RunDismButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RepairResultTextBlock.Text = _repairToolsService.RunDismRestoreHealth();
            SetStatus("Started DISM.");
        }
        catch (Exception ex)
        {
            RepairResultTextBlock.Text = ex.Message;
            SetStatus("Could not start DISM.");
        }
    }

    private void OpenWindowsUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RepairResultTextBlock.Text = _repairToolsService.OpenWindowsUpdate();
            SetStatus("Opened Windows Update.");
        }
        catch (Exception ex)
        {
            RepairResultTextBlock.Text = ex.Message;
            SetStatus("Could not open Windows Update.");
        }
    }

    private void OpenTempFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        FileToolsResultTextBlock.Text = _fileToolsService.OpenTempFolder();
        SetStatus("Opened temp folder.");
    }

    private void OpenDownloadsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        FileToolsResultTextBlock.Text = _fileToolsService.OpenDownloadsFolder();
        SetStatus("Opened downloads folder.");
    }

    private void OpenDesktopFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        FileToolsResultTextBlock.Text = _fileToolsService.OpenDesktopFolder();
        SetStatus("Opened desktop folder.");
    }

    private async void HashFileButton_OnClick(object sender, RoutedEventArgs e) => await HashSelectedFileAsync();

    private async void SendWesAiButton_OnClick(object sender, RoutedEventArgs e) => await SendWesAiMessageAsync();
    private void ClearWesAiButton_OnClick(object sender, RoutedEventArgs e) => ResetWesAiConversation();

    private async void WesAiInputTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_settings.SendAiOnEnter && e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            await SendWesAiMessageAsync();
        }
    }

    private void PreviousThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.ThemeIndex--;
        NormalizeSettings();
        ApplyTheme();
        SaveSettings();
    }

    private void PreviousInterfaceAnimationButton_OnClick(object sender, RoutedEventArgs e)
    {
        CycleInterfaceAnimation(-1);
    }

    private void NextInterfaceAnimationButton_OnClick(object sender, RoutedEventArgs e)
    {
        CycleInterfaceAnimation(1);
    }

    private void PreviousAltAnimationButton_OnClick(object sender, RoutedEventArgs e)
    {
        CycleAltAnimation(-1);
    }

    private void NextAltAnimationButton_OnClick(object sender, RoutedEventArgs e)
    {
        CycleAltAnimation(1);
    }

    private void NextThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.ThemeIndex++;
        NormalizeSettings();
        ApplyTheme();
        SaveSettings();
    }

    private void SettingsControl_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settings.RefreshOnStartup = RefreshOnStartupCheckBox.IsChecked == true;
        _settings.EnableAnimations = EnableAnimationsCheckBox.IsChecked == true;
        _settings.RefreshServicesAfterAction = RefreshServicesAfterActionCheckBox.IsChecked == true;
        _settings.EnableLeftAltShortcut = EnableLeftAltShortcutCheckBox.IsChecked == true;
        _settings.AutoRefreshRunningApps = AutoRefreshRunningAppsCheckBox.IsChecked == true;
        _settings.RememberAiHistory = RememberAiHistoryCheckBox.IsChecked == true;
        _settings.ShowAppPaths = ShowAppPathsCheckBox.IsChecked == true;
        _settings.SendAiOnEnter = SendAiOnEnterCheckBox.IsChecked == true;
        _settings.StartSystemMonitorLive = StartSystemMonitorLiveCheckBox.IsChecked == true;
        _settings.OpenCaptureFolderAfterScreenshot = OpenCaptureFolderAfterScreenshotCheckBox.IsChecked == true;
        _settings.StartConsoleWhenOpened = StartConsoleWhenOpenedCheckBox.IsChecked == true;
        Notify(nameof(AppPathVisibility));
        SaveSettings();
    }

    private void OpenAiApiKeyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settings.OpenAiApiKey = OpenAiApiKeyPasswordBox.Password.Trim();
        SaveSettings();
    }

    private void OpenAiModelTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settings.OpenAiModel = OpenAiModelTextBox.Text.Trim();
        SaveSettings();
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_settings.EnableAnimations)
        {
            SidebarPanel.Opacity = 1;
            HeroPanel.Opacity = 1;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookType, LowLevelKeyboardProc callback, nint moduleHandle, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);
}
