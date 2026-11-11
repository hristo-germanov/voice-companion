namespace ChatGPTVoiceCompanion.Models;

public sealed class ConversationSession
{
    public int Id { get; set; }
    public string Site { get; set; } = "chatgpt";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
}
