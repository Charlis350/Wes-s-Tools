namespace WessTools.Models;

public sealed class ChatMessage
{
    public string Role { get; init; } = "assistant";

    public string DisplayName { get; init; } = "Wes";

    public string Content { get; init; } = string.Empty;

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
}
