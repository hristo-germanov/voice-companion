using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using ChatGPTVoiceCompanion.Options;

namespace ChatGPTVoiceCompanion.Services;

public sealed class KokoroSpeechClient : IHostedService, IDisposable
{
    private readonly SpeechOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<KokoroSpeechClient> _logger;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private Process? _process;
    private Task? _ready;

    public KokoroSpeechClient(IOptions<SpeechOptions> options, IHostEnvironment environment,
        IHttpClientFactory http, ILogger<KokoroSpeechClient> logger)
    {
        _options = options.Value; _environment = environment; _http = http; _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // Warm in the background so the dashboard/API can start immediately.
        _ready = WarmAsync(ct);
        return Task.CompletedTask;
    }

    private async Task WarmAsync(CancellationToken ct)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(_options.KokoroPythonPath, _environment.ContentRootPath))
        {
            WorkingDirectory = _environment.ContentRootPath,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.GetFullPath(_options.KokoroWorkerPath, _environment.ContentRootPath));
        start.ArgumentList.Add(_options.KokoroPort.ToString());
        start.Environment["KOKORO_TOKEN"] = _token;
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Kokoro.");
        _ = DrainErrorsAsync(_process);
        var ready = await _process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(90), ct);
        if (ready != "READY") throw new InvalidOperationException("Kokoro could not load. Check the local model files and Python environment.");
        _logger.LogInformation("Kokoro ready: Heart US and Bella US.");
    }

    private async Task DrainErrorsAsync(Process process)
    {
        while (await process.StandardError.ReadLineAsync() is { } line)
            _logger.LogDebug("Kokoro: {Details}", line);
    }

    public async Task<BufferedAudioChunk> GenerateAsync(string text, string voice, double speed, string path, CancellationToken ct)
    {
        await (_ready ?? throw new InvalidOperationException("Kokoro has not started.")).WaitAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_options.KokoroPort}/synthesize");
        request.Headers.Add("X-Kokoro-Token", _token);
        // BaseHTTPRequestHandler requires Content-Length, rather than chunked JSON.
        request.Content = new StringContent(JsonSerializer.Serialize(new { text, voice, speed }), Encoding.UTF8, "application/json");
        using var http = _http.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Kokoro could not generate audio ({(int)response.StatusCode}).");
        var audio = await response.Content.ReadFromJsonAsync<AudioResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Kokoro returned no audio.");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(audio.Audio), ct);
        return new BufferedAudioChunk(path, audio.DurationSeconds, audio.Words);
    }

    private sealed record AudioResponse(string Audio, double DurationSeconds, SpeechWordTiming[] Words);

    public Task StopAsync(CancellationToken ct) { Dispose(); return Task.CompletedTask; }
    public void Dispose()
    {
        if (_process is { } process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.Dispose(); _process = null;
        }
    }
}
