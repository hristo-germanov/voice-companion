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
    private readonly Channel<SpeechItem> _channel = Channel.CreateUnbounded<SpeechItem>();
    private readonly SpeechOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SpeechQueue> _logger;
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
    private int _queuedItems;
    private volatile int _currentCharIndex;
    private volatile int _currentTextLength;
    private volatile int _currentSegmentIndex;
    private volatile int _totalSegments;
    private string? _currentSegmentText;

    public SpeechQueue(
        IOptions<SpeechOptions> options,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<SpeechQueue> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void Enqueue(string messageId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _channel.Writer.TryWrite(new SpeechItem(messageId, text));
        Interlocked.Increment(ref _queuedItems);
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
        _paused = false;
        _skipRequested = true;

        while (_channel.Reader.TryRead(out _))
        {
            // Drain queued items.
            Interlocked.Decrement(ref _queuedItems);
        }

        _isSpeaking = false;
        _currentMessageId = null;
        _currentText = null;
        _currentStartedAt = null;
        _currentCharIndex = 0;
        _currentTextLength = 0;
        _currentSegmentIndex = 0;
        _totalSegments = 0;
        _currentSegmentText = null;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _synthesizer?.SpeakAsyncCancelAll();
                _cloudPlayer?.Stop();
                _edgeOutput?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop speech queue.");
            }
        }
    }

    public void SkipCurrent()
    {
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
        if (!string.IsNullOrWhiteSpace(_lastSpoken) && !string.IsNullOrWhiteSpace(_lastMessageId))
        {
            _channel.Writer.TryWrite(new SpeechItem(_lastMessageId, _lastSpoken));
        }
    }

    public SpeechTrackerSnapshot GetTracker()
    {
        return new SpeechTrackerSnapshot(
            _currentMessageId,
            _currentText,
            _paused,
            _isSpeaking,
            Math.Max(0, _queuedItems),
            _currentStartedAt,
            _currentCharIndex,
            _currentTextLength,
            _currentSegmentIndex,
            _totalSegments,
            _currentSegmentText);
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

        while (await _channel.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                Interlocked.Decrement(ref _queuedItems);
                await WaitIfPaused(stoppingToken);
                try
                {
                    await SpeakItem(item, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to speak queued message {MessageId}.", item.MessageId);
                }
            }
        }
    }

    private async Task WaitIfPaused(CancellationToken ct)
    {
        while (_paused && !ct.IsCancellationRequested)
        {
            await Task.Delay(100, ct);
        }
    }

    private async Task SpeakItem(SpeechItem item, CancellationToken ct)
    {
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
        try
        {
            Directory.CreateDirectory(tempDir);
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

                var tempPath = Path.Combine(tempDir, $"seg-{i:D4}.mp3");
                var psi = new ProcessStartInfo
                {
                    FileName = "edge-tts",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("--voice");
                psi.ArgumentList.Add(settings.EdgeVoice);
                psi.ArgumentList.Add("--rate");
                var edgeRate = settings.EdgeRatePercent >= 0
                    ? $"+{settings.EdgeRatePercent}%"
                    : $"{settings.EdgeRatePercent}%";
                psi.ArgumentList.Add(edgeRate);
                psi.ArgumentList.Add("--text");
                psi.ArgumentList.Add(segment);
                psi.ArgumentList.Add("--write-media");
                psi.ArgumentList.Add(tempPath);

                using var process = Process.Start(psi);
                if (process is null)
                {
                    await MarkSpeechFailed(speechEventId, "Failed to start edge-tts process.", ct);
                    return;
                }

                await process.WaitForExitAsync(ct);
                if (process.ExitCode != 0)
                {
                    var stderr = await process.StandardError.ReadToEndAsync(ct);
                    await MarkSpeechFailed(
                        speechEventId,
                        $"edge-tts failed (exit {process.ExitCode}). {stderr}".Trim(),
                        ct);
                    return;
                }

                if (!File.Exists(tempPath))
                {
                    await MarkSpeechFailed(speechEventId, "edge-tts did not produce an audio file.", ct);
                    return;
                }

                await PlayEdgeAudio(tempPath, charOffset, segment.Length, ct);
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
            await MarkSpeechFailed(speechEventId, ex.Message, ct);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
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

        if (pieces.Count == 0)
        {
            return [normalized];
        }

        return pieces;
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
        var mode = _options.Mode?.Trim().ToLowerInvariant() == "cloud" ? "cloud" : "local";
        var rate = _options.Rate;
        var volume = _options.Volume;
        var voice = _options.Voice;
        var cloudApiKey = string.Empty;
        var cloudModel = _options.CloudModel;
        var cloudVoice = _options.CloudVoice;
        var cloudSpeed = _options.CloudSpeed;
        var edgeVoice = _options.EdgeVoice;
        var edgeRatePercent = _options.EdgeRatePercent;

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
                    x.Key == "speech.edge.rate_percent")
                .ToListAsync(ct);

            var modeRaw = settings.FirstOrDefault(x => x.Key == "speech.mode")?.Value?.Trim().ToLowerInvariant();
            if (modeRaw is "local" or "cloud" or "edge")
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
            if (int.TryParse(edgeRateRaw, out var parsedEdgeRate))
            {
                edgeRatePercent = parsedEdgeRate;
            }
        }

        return new RuntimeSpeechSettings(
            mode,
            Math.Clamp(rate, -10, 10),
            Math.Clamp(volume, 0, 100),
            voice,
            cloudApiKey,
            cloudModel,
            cloudVoice,
            Math.Clamp(cloudSpeed, 0.25, 4.0),
            edgeVoice,
            Math.Clamp(edgeRatePercent, -50, 100));
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

    private async Task MarkSpeechCompleted(string messageId, int? speechEventId, CancellationToken ct)
    {
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
        int EdgeRatePercent);

    private sealed record SpeechItem(string MessageId, string Text);
}
