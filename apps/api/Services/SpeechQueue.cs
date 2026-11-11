using System.Speech.Synthesis;
using System.Threading.Channels;
using System.Media;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using ChatGPTVoiceCompanion.Data;
using ChatGPTVoiceCompanion.Models;
using ChatGPTVoiceCompanion.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading;
using NAudio.Wave;

namespace ChatGPTVoiceCompanion.Services;

public sealed class SpeechQueue : BackgroundService, ISpeechQueue
{
    private readonly SpeechWorkQueue _work = new();
    private readonly object _bufferGate = new();
    private BufferedSpeech? _currentBuffer;
    private SpeechWordPosition? _currentWord;
    private double _positionSeconds;
    private SeekPosition? _seekPosition;
    private sealed record SeekPosition(double Seconds);
    private bool _playbackCompleted;
    private readonly SemaphoreSlim _progressGate = new(1, 1);
    private readonly SpeechOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpeechQueue> _logger;
    private readonly KokoroSpeechClient _kokoro;
    private SpeechSynthesizer? _synthesizer;
    private SoundPlayer? _cloudPlayer;
    private WaveOutEvent? _edgeOutput;
    private volatile bool _paused;
    private volatile bool _skipRequested;
    private string? _lastSpoken;
    private string? _lastMessageId;
    private volatile bool _isSpeaking;
    private string? _currentMessageId;
    private string? _currentText;
    private DateTimeOffset? _currentStartedAt;
    private volatile int _currentCharIndex;
    private volatile int _currentTextLength;
    private volatile int _currentSegmentIndex;
    private volatile int _totalSegments;
    private string? _currentSegmentText;
    private readonly object _playbackLock = new();
    private CancellationTokenSource? _currentPlaybackCancellation;
    private volatile bool _isPreparingAudio;
    private readonly HashSet<string> _deletedMessages = new();
    private string? _activeMessageId;
    private TaskCompletionSource<bool>? _activeFinished;

    public SpeechQueue(
        IOptions<SpeechOptions> options,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<SpeechQueue> logger,
        KokoroSpeechClient kokoro)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _kokoro = kokoro;
    }

    public void Enqueue(string messageId, string text, double startSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_playbackLock)
        {
            if (_deletedMessages.Contains(messageId)) return;
            _work.Add(new SpeechQueueEntry(messageId, text, startSeconds));
        }
        BufferNext();
    }

    public void Pause()
    {
        _paused = true;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _synthesizer?.Pause();
                if (_edgeOutput is { PlaybackState: PlaybackState.Playing })
                {
                    _edgeOutput.Pause();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to pause current speech item.");
            }
        }
    }

    public void Resume()
    {
        _paused = false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _synthesizer?.Resume();
                if (_edgeOutput is { PlaybackState: PlaybackState.Paused })
                {
                    _edgeOutput.Play();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resume current speech item.");
            }
        }
    }

    public void StopAll()
    {
        // Serialize stop/clear with dequeue so a waiting item cannot slip into playback.
        lock (_playbackLock)
        {
            _currentPlaybackCancellation?.Cancel();
            _paused = false;
            _skipRequested = true;
            ClearPending();
            ClearCurrentPlaybackState();
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _synthesizer?.SpeakAsyncCancelAll();
                    _cloudPlayer?.Stop();
                    _edgeOutput?.Stop();
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop speech queue."); }
            }
        }
    }

    public void SkipCurrent()
    {
        _paused = false;
        lock (_playbackLock) _currentPlaybackCancellation?.Cancel();
        _skipRequested = true;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _synthesizer?.SpeakAsyncCancelAll();
                _edgeOutput?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to skip current speech item.");
            }
        }
    }

    public void RepeatLast()
    {
        // Never append a second reading while one is active or already queued.
        if (_isSpeaking || _paused || _work.Count > 0)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(_lastSpoken) && !string.IsNullOrWhiteSpace(_lastMessageId))
        {
            Enqueue(_lastMessageId, _lastSpoken);
        }
    }

    public SpeechTrackerSnapshot GetTracker()
    {
        return new SpeechTrackerSnapshot(
            _currentMessageId,
            _currentText,
            _paused,
            _isSpeaking,
            _work.Count,
            _currentStartedAt,
            _currentCharIndex,
            _currentTextLength,
            _currentSegmentIndex,
            _totalSegments,
            _currentSegmentText,
            _isPreparingAudio,
            Volatile.Read(ref _currentWord),
            Volatile.Read(ref _seekPosition)?.Seconds ?? Volatile.Read(ref _positionSeconds),
            _currentBuffer?.BufferedSeconds ?? 0,
            _currentBuffer?.IsComplete ?? false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _synthesizer = new SpeechSynthesizer();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Speech synthesizer initialization failed. Speech output disabled.");
                _synthesizer = null;
            }
        }

        try
        {
            while (await _work.WaitAsync(stoppingToken))
            {
                await WaitIfPaused(stoppingToken);
                SpeechQueueEntry? item;
                CancellationTokenSource playbackCancellation;
                TaskCompletionSource<bool> finished;
                lock (_playbackLock)
                {
                    item = _work.Take();
                    if (item is null) continue;
                    playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _currentPlaybackCancellation = playbackCancellation;
                    _activeMessageId = item.MessageId;
                    finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _activeFinished = finished;
                }
                BufferNext();
                using (playbackCancellation)
                {
                    try { await SpeakItem(item, playbackCancellation.Token); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to read message {MessageId}.", item.MessageId); }
                    finally
                    {
                        try { await SaveProgressAsync(CancellationToken.None); }
                        catch (Exception error) { _logger.LogWarning(error, "Could not save playback position."); }
                        lock (_playbackLock)
                        {
                            _currentPlaybackCancellation = null;
                            _activeMessageId = null;
                            _activeFinished = null;
                            ClearCurrentPlaybackState();
                        }
                        await ReleaseAsync(item);
                        finished.TrySetResult(true);
                    }
                }
            }
        }
        finally { ClearPending(); }
    }

    private void BufferNext()
    {
        lock (_bufferGate)
        {
            var next = _work.Snapshot().FirstOrDefault();
            if (next is null || next.Buffer is not null) return;
            next.Buffer = PrepareAsync(next);
        }
    }

    private async Task<BufferedSpeech?> PrepareAsync(SpeechQueueEntry item)
    {
        var settings = await LoadRuntimeSettings(item.Cancellation.Token);
        if (settings.Mode != "kokoro") return null;
        return CreateBuffer(item, settings);
    }

    private BufferedSpeech CreateBuffer(SpeechQueueEntry item, RuntimeSpeechSettings settings) =>
        new(item.Text, settings.KokoroVoice, settings.KokoroSpeed,
            (part, path, ct) => _kokoro.GenerateAsync(part.Text, settings.KokoroVoice, settings.KokoroSpeed, path, ct),
            item.Cancellation.Token);

    private static async Task ReleaseAsync(SpeechQueueEntry item)
    {
        item.Cancellation.Cancel();
        try
        {
            if (item.Buffer is not null && await item.Buffer is { } buffer) await buffer.DisposeAsync();
        }
        catch (Exception) { /* Canceled/failed queued buffers are never replayed. */ }
    }

    public IReadOnlyList<QueuedSpeechSnapshot> GetPending() => _work.Snapshot().Select(item =>
    {
        var buffer = item.Buffer?.IsCompletedSuccessfully == true ? item.Buffer.Result : null;
        var status = item.Buffer?.IsFaulted == true ? "failed" : buffer?.Status ?? (item.Buffer is null ? "waiting" : "buffering");
        return new QueuedSpeechSnapshot(item.Id, item.MessageId, item.Text, status,
            buffer?.ReadyCount ?? 0, buffer?.Parts.Count ?? 0, buffer?.Voice);
    }).ToArray();

    public bool RemovePending(string id)
    {
        var item = _work.Remove(id);
        if (item is null) return false;
        _ = ReleaseAsync(item);
        BufferNext();
        return true;
    }

    public async Task RemoveMessageAsync(string messageId)
    {
        Task? finished = null;
        lock (_playbackLock)
        {
            _deletedMessages.Add(messageId);
            foreach (var entry in _work.Snapshot().Where(x => x.MessageId == messageId))
                RemovePending(entry.Id);
            if (_activeMessageId == messageId)
            {
                finished = _activeFinished?.Task;
                _currentPlaybackCancellation?.Cancel();
                _paused = false;
                _skipRequested = true;
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        _synthesizer?.SpeakAsyncCancelAll();
                        _cloudPlayer?.Stop();
                        _edgeOutput?.Stop();
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop deleted text."); }
                }
            }
        }
        // Finish event/progress writes before deleting the persisted record.
        if (finished is not null) await finished;
        lock (_playbackLock)
        {
            if (_lastMessageId == messageId) { _lastMessageId = null; _lastSpoken = null; }
        }
    }

    public void ClearPending()
    {
        foreach (var item in _work.Clear()) _ = ReleaseAsync(item);
    }

    public void SeekRelative(double seconds) => Seek((Volatile.Read(ref _seekPosition)?.Seconds ?? Volatile.Read(ref _positionSeconds)) + seconds);
    public void Seek(double seconds)
    {
        lock (_playbackLock)
        {
            var buffer = _currentBuffer;
            if (buffer is null || !double.IsFinite(seconds) || buffer.BufferedSeconds <= 0) return;
            var target = Math.Clamp(seconds, 0, Math.Max(0, buffer.BufferedSeconds - .001));
            Interlocked.Exchange(ref _seekPosition, new SeekPosition(target));
            Interlocked.Exchange(ref _positionSeconds, target);
            var (index, offset) = buffer.Locate(target);
            if (buffer.ReadyChunk(index) is { } chunk) UpdateWord(chunk, buffer.Parts[index].Start, offset);
            try { _edgeOutput?.Stop(); } catch (InvalidOperationException) { }
        }
    }

    private async Task WaitIfPaused(CancellationToken ct)
    {
        while (_paused && !ct.IsCancellationRequested)
        {
            await Task.Delay(100, ct);
        }
    }

    private async Task SpeakItem(SpeechQueueEntry item, CancellationToken ct)
    {
        _playbackCompleted = false;
        _skipRequested = false;
        _lastSpoken = item.Text;
        _lastMessageId = item.MessageId;
        _isSpeaking = true;
        _currentMessageId = item.MessageId;
        _currentText = item.Text;
        _currentStartedAt = DateTimeOffset.UtcNow;
        _currentCharIndex = 0;
        _currentTextLength = item.Text.Length;
        _currentSegmentIndex = 1;
        _totalSegments = 1;
        _currentSegmentText = item.Text;

        var speechEvent = await CreateSpeechEvent(item.MessageId, ct);
        var startedAt = DateTimeOffset.UtcNow;
        await MarkSpeechStarted(speechEvent, startedAt, ct);
        var runtimeSettings = await LoadRuntimeSettings(ct);

        if (runtimeSettings.Mode == "cloud")
        {
            await SpeakCloud(item.MessageId, item.Text, runtimeSettings, speechEvent, ct);
            ClearCurrentPlaybackState();
            return;
        }
        if (runtimeSettings.Mode == "kokoro")
        {
            await SpeakKokoro(item, runtimeSettings, speechEvent, ct);
            return;
        }
        if (runtimeSettings.Mode == "edge")
        {
            await SpeakEdge(item.MessageId, item.Text, runtimeSettings, speechEvent, ct);
            ClearCurrentPlaybackState();
            return;
        }

        if (!OperatingSystem.IsWindows() || _synthesizer is null)
        {
            await MarkSpeechFailed(speechEvent, "Local speech mode requires Windows speech synthesizer.", ct);
            ClearCurrentPlaybackState();
            return;
        }

        try
        {
            ApplyLocalOptions(_synthesizer, runtimeSettings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply speech options.");
            await MarkSpeechFailed(speechEvent, ex.Message, ct);
            ClearCurrentPlaybackState();
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, SpeakCompletedEventArgs args) => tcs.TrySetResult(true);
        void ProgressHandler(object? sender, SpeakProgressEventArgs args)
        {
            _currentCharIndex = Math.Clamp(args.CharacterPosition, 0, _currentTextLength);
            _currentSegmentIndex = 1;
            _totalSegments = 1;
            _currentSegmentText = args.Text;
        }

        _synthesizer.SpeakCompleted += Handler;
        _synthesizer.SpeakProgress += ProgressHandler;
        try
        {
            _synthesizer.SpeakAsync(item.Text);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task;

            if (_skipRequested)
            {
                await MarkSpeechSkipped(speechEvent, ct);
            }
            else
            {
                await MarkSpeechCompleted(item.MessageId, speechEvent, ct);
            }
        }
        catch (OperationCanceledException)
        {
            await MarkSpeechSkipped(speechEvent, ct);
        }
        catch (Exception ex)
        {
            await MarkSpeechFailed(speechEvent, ex.Message, ct);
            throw;
        }
        finally
        {
            _synthesizer.SpeakCompleted -= Handler;
            _synthesizer.SpeakProgress -= ProgressHandler;
            if (_skipRequested)
            {
                _skipRequested = false;
            }
            ClearCurrentPlaybackState();
        }
    }

    private void ClearCurrentPlaybackState()
    {
        _isPreparingAudio = false;
        _currentBuffer = null;
        _currentWord = null;
        _positionSeconds = 0;
        Interlocked.Exchange(ref _seekPosition, null);
        _isSpeaking = false;
        _currentMessageId = null;
        _currentText = null;
        _currentStartedAt = null;
        _currentCharIndex = 0;
        _currentTextLength = 0;
        _currentSegmentIndex = 0;
        _totalSegments = 0;
        _currentSegmentText = null;
    }

    private async Task SpeakKokoro(SpeechQueueEntry item, RuntimeSpeechSettings settings, int? eventId, CancellationToken ct)
    {
        try
        {
            _isPreparingAudio = true;
            var buffer = item.Buffer is null ? null : await item.Buffer.WaitAsync(ct);
            if (buffer is not null && (buffer.Voice != settings.KokoroVoice || buffer.Speed != settings.KokoroSpeed))
            {
                await buffer.DisposeAsync();
                buffer = null;
            }
            buffer ??= CreateBuffer(item, settings);
            item.Buffer = Task.FromResult<BufferedSpeech?>(buffer);
            lock (_playbackLock)
            {
                ct.ThrowIfCancellationRequested();
                _currentBuffer = buffer;
            }
            _totalSegments = buffer.Parts.Count;
            var index = 0;
            var offset = 0.0;
            if (item.StartSeconds > 0)
            {
                for (var part = 0; part < buffer.Parts.Count; part++)
                {
                    await buffer.GetAsync(part, ct);
                    if (buffer.BufferedSeconds > item.StartSeconds) break;
                }
                (index, offset) = buffer.Locate(item.StartSeconds);
                Interlocked.Exchange(ref _positionSeconds, buffer.StartSeconds(index) + offset);
            }
            while (index < buffer.Parts.Count)
            {
                if (Interlocked.Exchange(ref _seekPosition, null) is { } seek)
                {
                    (index, offset) = buffer.Locate(seek.Seconds);
                    Interlocked.Exchange(ref _positionSeconds, buffer.StartSeconds(index) + offset);
                }
                _currentSegmentIndex = index + 1;
                _currentSegmentText = buffer.Parts[index].Text;
                _isPreparingAudio = buffer.ReadyChunk(index) is null;
                var chunk = await buffer.GetAsync(index, ct);
                _isPreparingAudio = false;
                await WaitIfPaused(ct);
                ct.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _seekPosition) is not null) continue;
                await PlayTimedAudio(chunk, buffer.Parts[index].Start, buffer.StartSeconds(index), offset, ct);
                ct.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _seekPosition) is not null) continue;
                index++;
                offset = 0;
            }
            await MarkSpeechCompleted(item.MessageId, eventId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await MarkSpeechSkipped(eventId, CancellationToken.None);
        }
        catch (Exception error)
        {
            await MarkSpeechFailed(eventId, error.Message, CancellationToken.None);
        }
    }

    private void UpdateWord(BufferedAudioChunk chunk, int textStart, double seconds)
    {
        var word = chunk.Words.LastOrDefault(word => word.StartSeconds <= seconds && seconds < word.EndSeconds);
        _currentWord = word is null ? null : new SpeechWordPosition(textStart + word.Start, word.Length);
        if (word is not null) _currentCharIndex = Math.Clamp(textStart + word.Start, 0, _currentTextLength);
    }

    private async Task PlayTimedAudio(BufferedAudioChunk chunk, int textStart, double timeStart, double offset, CancellationToken ct)
    {
        using var reader = new AudioFileReader(chunk.Path);
        using var output = new WaveOutEvent { DesiredLatency = 100 };
        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, args) =>
        {
            if (args.Exception is not null) stopped.TrySetException(args.Exception);
            else stopped.TrySetResult(true);
        };
        reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(offset, 0, reader.TotalTime.TotalSeconds));
        output.Init(reader);
        _edgeOutput = output;
        try
        {
            output.Play();
            if (_paused) output.Pause();
            using var registration = ct.Register(() => { try { output.Stop(); } catch (InvalidOperationException) { } });
            var lastSave = DateTime.UtcNow;
            while (!stopped.Task.IsCompleted)
            {
                // Track samples actually played by the audio device, not the read-ahead buffer.
                var seconds = Math.Min(chunk.DurationSeconds, offset + output.GetPosition() / (double)reader.WaveFormat.AverageBytesPerSecond);
                if (Volatile.Read(ref _seekPosition) is null)
                {
                    Interlocked.Exchange(ref _positionSeconds, timeStart + seconds);
                    UpdateWord(chunk, textStart, seconds);
                }
                if (DateTime.UtcNow - lastSave > TimeSpan.FromSeconds(2))
                {
                    await SaveProgressAsync(ct);
                    lastSave = DateTime.UtcNow;
                }
                await Task.WhenAny(stopped.Task, Task.Delay(40, ct));
                ct.ThrowIfCancellationRequested();
            }
            await stopped.Task;
            if (Volatile.Read(ref _seekPosition) is null)
                Interlocked.Exchange(ref _positionSeconds, timeStart + Math.Min(chunk.DurationSeconds,
                    offset + output.GetPosition() / (double)reader.WaveFormat.AverageBytesPerSecond));
        }
        finally { _edgeOutput = null; }
    }

    private async Task SpeakCloud(
        string messageId,
        string text,
        RuntimeSpeechSettings settings,
        int? speechEventId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.CloudApiKey))
        {
            await MarkSpeechFailed(speechEventId, "Cloud speech mode requires speech.cloud.api_key.", ct);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            await MarkSpeechFailed(speechEventId, "Cloud playback currently requires Windows audio playback.", ct);
            return;
        }

        try
        {
            var segments = SplitIntoSegments(text);
            _totalSegments = Math.Max(1, segments.Count);

            var charOffset = 0;
            for (var i = 0; i < segments.Count; i += 1)
            {
                await WaitIfPaused(ct);
                var segment = segments[i];
                _currentSegmentIndex = i + 1;
                _currentSegmentText = segment;
                _currentCharIndex = Math.Min(charOffset, _currentTextLength);

                var bytes = await RequestOpenAiSpeech(segment, settings, ct);
                if (bytes.Length == 0)
                {
                    await MarkSpeechFailed(speechEventId, "Cloud speech returned empty audio response.", ct);
                    return;
                }

                using var stream = new MemoryStream(bytes);
                using var player = new SoundPlayer(stream);
                _cloudPlayer = player;
                player.Load();
                player.PlaySync();
                _cloudPlayer = null;

                charOffset += segment.Length;
                _currentCharIndex = Math.Min(charOffset, _currentTextLength);
                if (_skipRequested)
                {
                    await MarkSpeechSkipped(speechEventId, ct);
                    return;
                }
            }

            await MarkSpeechCompleted(messageId, speechEventId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud speech playback failed.");
            await MarkSpeechFailed(speechEventId, ex.Message, ct);
        }
    }

    private async Task SpeakEdge(
        string messageId,
        string text,
        RuntimeSpeechSettings settings,
        int? speechEventId,
        CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"edge-tts-{Guid.NewGuid():N}");
        using var generation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<string>? pending = null;
        try
        {
            Directory.CreateDirectory(tempDir);
            var segments = EdgeSpeechText.Split(text);
            if (segments.Count == 0)
            {
                await MarkSpeechFailed(speechEventId, "Text contains no readable letters or numbers.", ct);
                return;
            }
            _totalSegments = segments.Count;

            async Task<string> Generate(int index)
            {
                var path = Path.Combine(tempDir, $"seg-{index:D4}." + (settings.Mode == "kokoro" ? "wav" : "mp3"));
                if (settings.Mode == "kokoro")
                    await _kokoro.GenerateAsync(segments[index], settings.KokoroVoice, settings.KokoroSpeed, path, generation.Token);
                else
                    await EdgeAudioGenerator.GenerateAsync(segments[index], settings.EdgeVoice,
                        settings.EdgeRatePercent, path, generation.Token);
                return path;
            }

            pending = Generate(0);
            var charOffset = 0;
            for (var i = 0; i < segments.Count; i++)
            {
                _currentSegmentIndex = i + 1;
                _currentSegmentText = segments[i];
                _isPreparingAudio = true;
                var path = await pending!;
                _isPreparingAudio = false;
                pending = i + 1 < segments.Count ? Generate(i + 1) : null;
                await WaitIfPaused(ct);
                ct.ThrowIfCancellationRequested();
                await PlayEdgeAudio(path, charOffset, segments[i].Length, ct);
                charOffset += segments[i].Length;
                _currentCharIndex = Math.Min(charOffset, _currentTextLength);
                ct.ThrowIfCancellationRequested();
            }
            await MarkSpeechCompleted(messageId, speechEventId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await MarkSpeechSkipped(speechEventId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await MarkSpeechFailed(speechEventId, ex.Message, CancellationToken.None);
        }
        finally
        {
            generation.Cancel();
            // Join prefetch before removing files; no orphan synthesis processes on Stop.
            if (pending is not null)
            {
                try { await pending; }
                catch { /* The current error/cancellation is already reported. */ }
            }
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
            catch { /* Best effort cleanup. */ }
        }
    }

    private async Task PlayEdgeAudio(string path, int segmentStart, int segmentLength, CancellationToken ct)
    {
        using var reader = new AudioFileReader(path);
        using var output = new WaveOutEvent();
        _edgeOutput = output;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
        output.Init(reader);
        output.Play();
        using var reg = ct.Register(() =>
        {
            try
            {
                output.Stop();
            }
            catch
            {
                // Ignore cancellation stop errors.
            }
        });
        while (!tcs.Task.IsCompleted)
        {
            var totalMs = reader.TotalTime.TotalMilliseconds;
            var currentMs = reader.CurrentTime.TotalMilliseconds;

            if (totalMs > 0 && segmentLength > 0)
            {
                var ratio = Math.Clamp(currentMs / totalMs, 0, 1);
                var segmentProgress = (int)Math.Round(segmentLength * ratio);
                _currentCharIndex = Math.Min(segmentStart + segmentProgress, _currentTextLength);
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(70, ct));
            if (completed == tcs.Task)
            {
                break;
            }
        }

        await tcs.Task;
        _edgeOutput = null;
    }

    private static List<string> SplitIntoSegments(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var pieces = normalized
            .Split(['\n'], StringSplitOptions.None)
            .SelectMany(line => line.Split(['.', '!', '?'], StringSplitOptions.TrimEntries))
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return pieces.Count == 0 ? [normalized] : pieces;
    }

    private async Task<byte[]> RequestOpenAiSpeech(string text, RuntimeSpeechSettings settings, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CloudApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = settings.CloudModel,
                voice = settings.CloudVoice,
                input = text,
                format = "wav",
                speed = settings.CloudSpeed
            }),
            Encoding.UTF8,
            "application/json");

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var payload = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var textPayload = Encoding.UTF8.GetString(payload);
            throw new InvalidOperationException(
                $"OpenAI speech request failed ({(int)response.StatusCode}): {textPayload}");
        }

        return payload;
    }

    private void ApplyLocalOptions(SpeechSynthesizer synthesizer, RuntimeSpeechSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        synthesizer.Rate = Math.Clamp(settings.Rate, -10, 10);
        synthesizer.Volume = Math.Clamp(settings.Volume, 0, 100);

        if (!string.IsNullOrWhiteSpace(settings.Voice))
        {
            try
            {
                synthesizer.SelectVoice(settings.Voice);
            }
            catch (ArgumentException)
            {
                // Voice not available; keep default.
            }
        }
    }

    private async Task<RuntimeSpeechSettings> LoadRuntimeSettings(CancellationToken ct)
    {
        var mode = SpeechOptions.NormalizeMode(_options.Mode);
        var rate = _options.Rate;
        var volume = _options.Volume;
        var voice = _options.Voice;
        var cloudApiKey = string.Empty;
        var cloudModel = _options.CloudModel;
        var cloudVoice = _options.CloudVoice;
        var cloudSpeed = _options.CloudSpeed;
        var edgeVoice = _options.EdgeVoice;
        var edgeRatePercent = _options.EdgeRatePercent;
        var kokoroVoice = _options.KokoroVoice;
        var kokoroSpeed = _options.KokoroSpeed;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = await db.AppSettings
                .AsNoTracking()
                .Where(x =>
                    x.Key == "speech.mode" ||
                    x.Key == "speech.rate" ||
                    x.Key == "speech.volume" ||
                    x.Key == "speech.voice" ||
                    x.Key == "speech.cloud.api_key" ||
                    x.Key == "speech.cloud.model" ||
                    x.Key == "speech.cloud.voice" ||
                    x.Key == "speech.cloud.speed" ||
                    x.Key == "speech.edge.voice" ||
                    x.Key == "speech.edge.rate_percent" ||
                    x.Key == "speech.kokoro.voice" || x.Key == "speech.kokoro.speed")
                .ToListAsync(ct);

            var modeRaw = settings.FirstOrDefault(x => x.Key == "speech.mode")?.Value?.Trim().ToLowerInvariant();
            if (modeRaw is "local" or "cloud" or "edge" or "kokoro")
            {
                mode = modeRaw;
            }

            var rateRaw = settings.FirstOrDefault(x => x.Key == "speech.rate")?.Value;
            if (int.TryParse(rateRaw, out var parsedRate))
            {
                rate = parsedRate;
            }

            var volumeRaw = settings.FirstOrDefault(x => x.Key == "speech.volume")?.Value;
            if (int.TryParse(volumeRaw, out var parsedVolume))
            {
                volume = parsedVolume;
            }

            var voiceRaw = settings.FirstOrDefault(x => x.Key == "speech.voice")?.Value;
            if (!string.IsNullOrWhiteSpace(voiceRaw))
            {
                voice = voiceRaw;
            }

            var cloudApiKeyRaw = settings.FirstOrDefault(x => x.Key == "speech.cloud.api_key")?.Value;
            if (!string.IsNullOrWhiteSpace(cloudApiKeyRaw))
            {
                cloudApiKey = cloudApiKeyRaw.Trim();
            }

            var cloudModelRaw = settings.FirstOrDefault(x => x.Key == "speech.cloud.model")?.Value;
            if (!string.IsNullOrWhiteSpace(cloudModelRaw))
            {
                cloudModel = cloudModelRaw.Trim();
            }

            var cloudVoiceRaw = settings.FirstOrDefault(x => x.Key == "speech.cloud.voice")?.Value;
            if (!string.IsNullOrWhiteSpace(cloudVoiceRaw))
            {
                cloudVoice = cloudVoiceRaw.Trim();
            }

            var cloudSpeedRaw = settings.FirstOrDefault(x => x.Key == "speech.cloud.speed")?.Value;
            if (double.TryParse(cloudSpeedRaw, out var parsedCloudSpeed))
            {
                cloudSpeed = parsedCloudSpeed;
            }

            var edgeVoiceRaw = settings.FirstOrDefault(x => x.Key == "speech.edge.voice")?.Value;
            if (!string.IsNullOrWhiteSpace(edgeVoiceRaw))
            {
                edgeVoice = edgeVoiceRaw.Trim();
            }

            var edgeRateRaw = settings.FirstOrDefault(x => x.Key == "speech.edge.rate_percent")?.Value;
            kokoroVoice = settings.FirstOrDefault(x => x.Key == "speech.kokoro.voice")?.Value ?? kokoroVoice;
            if (double.TryParse(settings.FirstOrDefault(x => x.Key == "speech.kokoro.speed")?.Value,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedKokoroSpeed))
                kokoroSpeed = parsedKokoroSpeed;
            if (int.TryParse(edgeRateRaw, out var parsedEdgeRate))
            {
                edgeRatePercent = parsedEdgeRate;
            }
        }

        return new RuntimeSpeechSettings(
            SpeechOptions.NormalizeMode(mode),
            Math.Clamp(rate, -10, 10),
            Math.Clamp(volume, 0, 100),
            voice,
            cloudApiKey,
            cloudModel,
            cloudVoice,
            Math.Clamp(cloudSpeed, 0.25, 4.0),
            edgeVoice,
            Math.Clamp(edgeRatePercent, -50, 100),
            kokoroVoice == "af_bella" ? "af_bella" : "af_heart",
            double.IsFinite(kokoroSpeed) ? Math.Clamp(kokoroSpeed, 0.5, 2.0) : 1.0);
    }

    private async Task<int?> CreateSpeechEvent(string messageId, CancellationToken ct)
    {
        if (!int.TryParse(messageId, out var parsedMessageId))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = new SpeechEvent
        {
            MessageId = parsedMessageId,
            Status = "queued"
        };
        db.SpeechEvents.Add(evt);
        await db.SaveChangesAsync(ct);
        return evt.Id;
    }

    private async Task MarkSpeechStarted(int? speechEventId, DateTimeOffset startedAt, CancellationToken ct)
    {
        if (speechEventId is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.SpeechEvents.FirstOrDefaultAsync(x => x.Id == speechEventId.Value, ct);
        if (evt is null)
        {
            return;
        }

        evt.Status = "started";
        evt.StartedAt = startedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveProgressAsync(CancellationToken ct = default)
    {
        await _progressGate.WaitAsync(ct);
        try
        {
            if (_playbackCompleted || !int.TryParse(_currentMessageId, out var id)) return;
            var snapshot = GetTracker();
            var percent = snapshot.IsFullyBuffered && snapshot.BufferedSeconds > 0
                ? snapshot.PositionSeconds / snapshot.BufferedSeconds * 100
                : snapshot.CurrentTextLength > 0 ? snapshot.CurrentCharIndex / (double)snapshot.CurrentTextLength * 100 : 0;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.CapturedMessages.FirstOrDefaultAsync(item => item.Id == id, ct);
            if (message is null) return;
            message.PlaybackPositionSeconds = Math.Max(0, snapshot.PositionSeconds);
            if (snapshot.IsFullyBuffered) message.PlaybackDurationSeconds = snapshot.BufferedSeconds;
            message.PlaybackProgressPercent = Math.Clamp((int)percent, 0, 99);
            message.PlaybackCompleted = false;
            await db.SaveChangesAsync(ct);
        }
        finally { _progressGate.Release(); }
    }

    private async Task MarkSpeechCompleted(string messageId, int? speechEventId, CancellationToken ct)
    {
        _playbackCompleted = true;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        if (speechEventId is not null)
        {
            var evt = await db.SpeechEvents.FirstOrDefaultAsync(x => x.Id == speechEventId.Value, ct);
            if (evt is not null)
            {
                evt.Status = "completed";
                evt.EndedAt = now;
                evt.Error = null;
            }
        }

        if (int.TryParse(messageId, out var parsedMessageId))
        {
            var message = await db.CapturedMessages.FirstOrDefaultAsync(x => x.Id == parsedMessageId, ct);
            if (message is not null)
            {
                message.SpokeAt = now;
                message.PlaybackCompleted = true;
                message.PlaybackProgressPercent = 100;
                message.PlaybackPositionSeconds = _currentBuffer?.BufferedSeconds ?? _positionSeconds;
                message.PlaybackDurationSeconds = message.PlaybackPositionSeconds;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task MarkSpeechSkipped(int? speechEventId, CancellationToken ct)
    {
        if (speechEventId is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.SpeechEvents.FirstOrDefaultAsync(x => x.Id == speechEventId.Value, ct);
        if (evt is null)
        {
            return;
        }

        evt.Status = "skipped";
        evt.EndedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkSpeechFailed(int? speechEventId, string error, CancellationToken ct)
    {
        if (speechEventId is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.SpeechEvents.FirstOrDefaultAsync(x => x.Id == speechEventId.Value, ct);
        if (evt is null)
        {
            return;
        }

        evt.Status = "failed";
        evt.EndedAt = DateTimeOffset.UtcNow;
        evt.Error = error;
        await db.SaveChangesAsync(ct);
    }

    private sealed record RuntimeSpeechSettings(
        string Mode,
        int Rate,
        int Volume,
        string Voice,
        string CloudApiKey,
        string CloudModel,
        string CloudVoice,
        double CloudSpeed,
        string EdgeVoice,
        int EdgeRatePercent,
        string KokoroVoice,
        double KokoroSpeed);

}
