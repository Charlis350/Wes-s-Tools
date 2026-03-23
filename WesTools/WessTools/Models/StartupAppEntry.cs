using System.IO;
using System.Windows.Media;

namespace WessTools.Models;

public sealed class StartupAppEntry
{
    public string Name { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public string ResolvedPath { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string TileGlyph { get; init; } = "S";

    public ImageSource? IconSource { get; init; }

    public bool HasLaunchTarget => !string.IsNullOrWhiteSpace(ResolvedPath) && File.Exists(ResolvedPath);
}
