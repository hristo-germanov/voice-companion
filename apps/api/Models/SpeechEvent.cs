namespace ChatGPTVoiceCompanion.Models;

public sealed class SpeechEvent
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string Status { get; set; } = "queued";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Error { get; set; }
}
