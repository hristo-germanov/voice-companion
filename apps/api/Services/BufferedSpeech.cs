namespace ChatGPTVoiceCompanion.Services;

internal sealed record SpeechTextPart(string Text, int Start);
public sealed record SpeechWordTiming(int Start, int Length, double StartSeconds, double EndSeconds);
public sealed record BufferedAudioChunk(string Path, double DurationSeconds, IReadOnlyList<SpeechWordTiming> Words);

internal sealed class BufferedSpeech : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly TaskCompletionSource<BufferedAudioChunk>[] _chunks;
    private readonly object _gate = new();
    private int _readyCount;
    private double _bufferedSeconds;
    public IReadOnlyList<SpeechTextPart> Parts { get; }
    public string Voice { get; }
    public double Speed { get; }
    public string DirectoryPath { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"speech-buffer-{Guid.NewGuid():N}");
    public Task Completion { get; }
    public int ReadyCount { get { lock (_gate) return _readyCount; } }
    public double BufferedSeconds { get { lock (_gate) return _bufferedSeconds; } }
    public bool IsComplete => ReadyCount == Parts.Count;
    public string Status => Completion.IsFaulted ? "failed" : IsComplete ? "ready" : "buffering";

    public BufferedSpeech(string text, string voice, double speed,
        Func<SpeechTextPart, string, CancellationToken, Task<BufferedAudioChunk>> generate, CancellationToken ct)
    {
        Voice = voice; Speed = speed;
        Parts = SpeechTextParts.Split(text);
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _chunks = Parts.Select(_ => new TaskCompletionSource<BufferedAudioChunk>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        System.IO.Directory.CreateDirectory(DirectoryPath);
        Completion = GenerateAsync(generate);
        // Observe failures even when a queued item is removed before playback starts.
        _ = Completion.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task GenerateAsync(Func<SpeechTextPart, string, CancellationToken, Task<BufferedAudioChunk>> generate)
    {
        try
        {
            if (Parts.Count == 0) throw new InvalidOperationException("Text contains no readable words.");
            for (var i = 0; i < Parts.Count; i++)
            {
                var audio = await generate(Parts[i], System.IO.Path.Combine(DirectoryPath, $"{i:D4}.wav"), _cancellation.Token);
                lock (_gate) { _readyCount++; _bufferedSeconds += audio.DurationSeconds; }
                _chunks[i].TrySetResult(audio);
            }
        }
        catch (Exception error)
        {
            foreach (var chunk in _chunks)
            {
                chunk.TrySetException(error);
                _ = chunk.Task.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
            throw;
        }
    }

    public Task<BufferedAudioChunk> GetAsync(int index, CancellationToken ct) => _chunks[index].Task.WaitAsync(ct);
    public BufferedAudioChunk? ReadyChunk(int index) => _chunks[index].Task.IsCompletedSuccessfully ? _chunks[index].Task.Result : null;

    public (int Index, double Offset) Locate(double seconds)
    {
        var position = Math.Clamp(seconds, 0, Math.Max(0, BufferedSeconds - .001));
        for (var i = 0; i < ReadyCount; i++)
        {
            var chunk = ReadyChunk(i);
            if (chunk is null) break;
            if (position < chunk.DurationSeconds) return (i, position);
            position -= chunk.DurationSeconds;
        }
        return (0, 0);
    }

    public double StartSeconds(int index) => Enumerable.Range(0, index).Sum(i => ReadyChunk(i)?.DurationSeconds ?? 0);

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        try { await Completion; } catch { }
        _cancellation.Dispose();
        try { System.IO.Directory.Delete(DirectoryPath, recursive: true); } catch (IOException) { }
    }
}

internal static class SpeechTextParts
{
    public static List<SpeechTextPart> Split(string text)
    {
        var parts = new List<SpeechTextPart>();
        for (var offset = 0; offset < text.Length;)
        {
            var limit = parts.Count == 0 && text.Length > 1000 ? 400 : 1000;
            var end = Math.Min(text.Length, offset + limit);
            if (end < text.Length)
            {
                var boundary = end;
                while (boundary > offset + limit / 2 && !char.IsWhiteSpace(text[boundary - 1])) boundary--;
                if (boundary > offset + limit / 2) end = boundary;
                if (char.IsHighSurrogate(text[end - 1])) end--;
            }
            var piece = text[offset..end];
            if (piece.Any(char.IsLetterOrDigit)) parts.Add(new SpeechTextPart(piece, offset));
            offset = end;
        }
        return parts;
    }
}
