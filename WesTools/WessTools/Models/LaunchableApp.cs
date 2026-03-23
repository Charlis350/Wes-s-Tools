using System.Windows.Media;

namespace WessTools.Models;

public sealed class LaunchableApp
{
    public required string Name { get; init; }

    public required string ExecutablePath { get; init; }

    public required string SourceLabel { get; init; }

    public required string Publisher { get; init; }

    public string? InstallLocation { get; init; }

    public bool IsGame { get; init; }

    public ImageSource? IconSource { get; init; }

    public bool HasIcon => IconSource is not null;

    public string TileGlyph => string.IsNullOrWhiteSpace(Name) ? "A" : char.ToUpperInvariant(Name[0]).ToString();

    public string BadgeText => IsGame ? "Game Ready" : "App Verified";

    public string Subtitle
    {
        get
        {
            var location = string.IsNullOrWhiteSpace(InstallLocation) ? SourceLabel : InstallLocation;
            return $"{Publisher} - {location}";
        }
    }
}
