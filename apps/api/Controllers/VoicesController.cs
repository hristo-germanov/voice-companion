using Microsoft.AspNetCore.Mvc;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using System.Diagnostics;
using System.Text.Json;

namespace ChatGPTVoiceCompanion.Controllers;

[ApiController]
[Route("api/voices")]
public sealed class VoicesController : ControllerBase
{
    private static readonly HashSet<string> EnUsLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        "en-US"
    };

    private readonly ILogger<VoicesController> _logger;

    public VoicesController(ILogger<VoicesController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Ok(Array.Empty<object>());
        }

        try
        {
            return Ok(GetWindowsVoices());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate installed speech voices.");
            return Ok(Array.Empty<object>());
        }
    }

    [HttpGet("edge-en-us")]
    [HttpGet("edge-en-eu")]
    public async Task<IActionResult> GetEdgeEnglishUnitedStates(CancellationToken ct)
    {
        try
        {
            var voices = await GetEdgeEnUsVoices(ct);
            if (voices.Count == 0)
            {
                return Ok(FallbackEdgeEnUsVoices());
            }
            return Ok(voices);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate edge-tts English US voices.");
            return Ok(FallbackEdgeEnUsVoices());
        }
    }

    private static async Task<List<object>> GetEdgeEnUsVoices(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "edge-tts",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--list-voices");

        using var process = Process.Start(psi);
        if (process is null)
        {
            return FallbackEdgeEnUsVoices();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return FallbackEdgeEnUsVoices();
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return FallbackEdgeEnUsVoices();
            }

            var list = new List<EdgeVoiceInfo>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("ShortName", out var shortNameEl))
                {
                    continue;
                }

                var shortName = shortNameEl.GetString() ?? string.Empty;
                if (!shortName.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var locale = item.TryGetProperty("Locale", out var localeEl)
                    ? localeEl.GetString() ?? string.Empty
                    : string.Empty;
                if (!EnUsLocales.Contains(locale))
                {
                    continue;
                }

                if (!shortName.StartsWith($"{locale}-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var gender = item.TryGetProperty("Gender", out var genderEl)
                    ? genderEl.GetString() ?? string.Empty
                    : string.Empty;
                var friendlyName = item.TryGetProperty("FriendlyName", out var friendlyEl)
                    ? friendlyEl.GetString() ?? shortName
                    : shortName;

                list.Add(new EdgeVoiceInfo(shortName, locale, gender, friendlyName));
            }

            return list
                .DistinctBy(x => x.ShortName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.ShortName, StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToList();
        }
        catch
        {
            return FallbackEdgeEnUsVoices();
        }
    }

    private static List<object> FallbackEdgeEnUsVoices()
    {
        var known = new[]
        {
            new EdgeVoiceInfo("en-US-AdamMultilingualNeural", "en-US", "Male", "en-US-AdamMultilingualNeural"),
            new EdgeVoiceInfo("en-US-AlloyTurboMultilingualNeural", "en-US", "", "en-US-AlloyTurboMultilingualNeural"),
            new EdgeVoiceInfo("en-US-AmandaMultilingualNeural", "en-US", "Female", "en-US-AmandaMultilingualNeural"),
            new EdgeVoiceInfo("en-US-AndrewMultilingualNeural", "en-US", "Male", "en-US-AndrewMultilingualNeural"),
            new EdgeVoiceInfo("en-US-AndrewNeural", "en-US", "Male", "en-US-AndrewNeural"),
            new EdgeVoiceInfo("en-US-AriaNeural", "en-US", "Female", "en-US-AriaNeural"),
            new EdgeVoiceInfo("en-US-AvaMultilingualNeural", "en-US", "Female", "en-US-AvaMultilingualNeural"),
            new EdgeVoiceInfo("en-US-AvaNeural", "en-US", "Female", "en-US-AvaNeural"),
            new EdgeVoiceInfo("en-US-BrianMultilingualNeural", "en-US", "Male", "en-US-BrianMultilingualNeural"),
            new EdgeVoiceInfo("en-US-BrianNeural", "en-US", "Male", "en-US-BrianNeural"),
            new EdgeVoiceInfo("en-US-DavisNeural", "en-US", "Male", "en-US-DavisNeural"),
            new EdgeVoiceInfo("en-US-EchoTurboMultilingualNeural", "en-US", "", "en-US-EchoTurboMultilingualNeural"),
            new EdgeVoiceInfo("en-US-EmmaMultilingualNeural", "en-US", "Female", "en-US-EmmaMultilingualNeural"),
            new EdgeVoiceInfo("en-US-EmmaNeural", "en-US", "Female", "en-US-EmmaNeural"),
            new EdgeVoiceInfo("en-US-FableTurboMultilingualNeural", "en-US", "", "en-US-FableTurboMultilingualNeural"),
            new EdgeVoiceInfo("en-US-GuyNeural", "en-US", "Male", "en-US-GuyNeural"),
            new EdgeVoiceInfo("en-US-JaneNeural", "en-US", "Female", "en-US-JaneNeural"),
            new EdgeVoiceInfo("en-US-JasonNeural", "en-US", "Male", "en-US-JasonNeural"),
            new EdgeVoiceInfo("en-US-JennyNeural", "en-US", "Female", "en-US-JennyNeural"),
            new EdgeVoiceInfo("en-US-KaiNeural", "en-US", "Male", "en-US-KaiNeural"),
            new EdgeVoiceInfo("en-US-LunaNeural", "en-US", "Female", "en-US-LunaNeural"),
            new EdgeVoiceInfo("en-US-NovaTurboMultilingualNeural", "en-US", "", "en-US-NovaTurboMultilingualNeural"),
            new EdgeVoiceInfo("en-US-OnyxTurboMultilingualNeural", "en-US", "", "en-US-OnyxTurboMultilingualNeural"),
            new EdgeVoiceInfo("en-US-PhoebeMultilingualNeural", "en-US", "Female", "en-US-PhoebeMultilingualNeural"),
            new EdgeVoiceInfo("en-US-ShimmerTurboMultilingualNeural", "en-US", "", "en-US-ShimmerTurboMultilingualNeural")
        };

        return known
            .OrderBy(x => x.ShortName, StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToList();
    }

    private sealed record EdgeVoiceInfo(
        string ShortName,
        string Locale,
        string Gender,
        string FriendlyName);

    [SupportedOSPlatform("windows")]
    private static List<object> GetWindowsVoices()
    {
        using var synth = new SpeechSynthesizer();
        return synth.GetInstalledVoices()
            .Select(v => v.VoiceInfo)
            .Select(v => new
            {
                name = v.Name,
                gender = v.Gender.ToString(),
                culture = v.Culture.Name,
                age = v.Age.ToString()
            })
            .Cast<object>()
            .ToList();
    }
}
