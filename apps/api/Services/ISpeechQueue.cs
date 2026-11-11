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
    string? CurrentSegmentText,
    bool IsPreparingAudio = false,
    SpeechWordPosition? CurrentWord = null,
    double PositionSeconds = 0,
    double BufferedSeconds = 0,
    bool IsFullyBuffered = false);

public sealed record SpeechWordPosition(int Start, int Length);
public sealed record QueuedSpeechSnapshot(string Id, string MessageId, string Text, string Status,
    int BufferedParts, int TotalParts, string? Voice);

public interface ISpeechQueue
{
    void Enqueue(string messageId, string text, double startSeconds = 0);
    void Pause();
    void Resume();
    void StopAll();
    void SkipCurrent();
    void RepeatLast();
    SpeechTrackerSnapshot GetTracker();
    IReadOnlyList<QueuedSpeechSnapshot> GetPending();
    bool RemovePending(string id);
    void ClearPending();
    Task RemoveMessageAsync(string messageId);
    void Seek(double seconds);
    void SeekRelative(double seconds);
    Task SaveProgressAsync(CancellationToken ct = default);
}
