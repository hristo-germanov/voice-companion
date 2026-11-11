namespace ChatGPTVoiceCompanion.Services;

public sealed record SpeechTrackerSnapshot(
    string? CurrentMessageId,
    string? CurrentText,
    bool IsPaused,
    bool IsSpeaking,
    int QueuedItems,
    DateTimeOffset? StartedAt,
    int CurrentCharIndex,
    int CurrentTextLength,
    int CurrentSegmentIndex,
    int TotalSegments,
    string? CurrentSegmentText);

public interface ISpeechQueue
{
    void Enqueue(string messageId, string text);
    void Pause();
    void Resume();
    void StopAll();
    void SkipCurrent();
    void RepeatLast();
    SpeechTrackerSnapshot GetTracker();
}
