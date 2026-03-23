using System.Windows.Media;

namespace WessTools.Models;

public sealed class ThemePalette
{
    public required string Name { get; init; }

    public required Color Background { get; init; }

    public required Color Sidebar { get; init; }

    public required Color Panel { get; init; }

    public required Color PanelAlt { get; init; }

    public required Color Accent { get; init; }

    public required Color AccentAlt { get; init; }

    public required Color TextPrimary { get; init; }

    public required Color TextSecondary { get; init; }

    public required Color HeroStart { get; init; }

    public required Color HeroMiddle { get; init; }

    public required Color HeroEnd { get; init; }
}
