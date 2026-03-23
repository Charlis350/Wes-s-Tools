using System.Windows.Media;

namespace WessTools.Models;

public sealed class RunningAppEntry
{
    public required string Name { get; init; }

    public required int ProcessId { get; init; }

    public required string WindowTitle { get; init; }

    public required string ExecutablePath { get; init; }

    public required string MemoryText { get; init; }

    public ImageSource? IconSource { get; init; }

    public bool HasIcon => IconSource is not null;

    public string TileGlyph => string.IsNullOrWhiteSpace(Name) ? "R" : char.ToUpperInvariant(Name[0]).ToString();
}
