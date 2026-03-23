namespace WessTools.Models;

public sealed class ServiceEntry
{
    public required string ServiceName { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string StartMode { get; init; }

    public required string StatusText { get; init; }

    public bool CanStop { get; init; }

    public bool CanStart => !string.Equals(StatusText, "Running", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(StatusText, "Start Pending", StringComparison.OrdinalIgnoreCase);

    public bool CanRestart => string.Equals(StatusText, "Running", StringComparison.OrdinalIgnoreCase);
}
