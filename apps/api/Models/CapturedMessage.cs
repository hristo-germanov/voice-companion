namespace ChatGPTVoiceCompanion.Models;

public sealed class CapturedMessage
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string Role { get; set; } = "assistant";
    public string Text { get; set; } = string.Empty;
    public string TextHash { get; set; } = string.Empty;
    public string? SourceMessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SpokeAt { get; set; }
}
