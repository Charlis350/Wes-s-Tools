using System.IO;
using System.Text.Json;
using WessTools.Models;

namespace WessTools.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var baseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WessTools");
        Directory.CreateDirectory(baseFolder);
        _settingsPath = Path.Combine(baseFolder, "settings.json");
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }
}
