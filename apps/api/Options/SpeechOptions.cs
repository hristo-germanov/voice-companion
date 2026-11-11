namespace ChatGPTVoiceCompanion.Options;

public sealed class SpeechOptions
{
    public const string SectionName = "Speech";
    public static string NormalizeMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "local" => "local",
        "cloud" => "cloud",
        _ => "kokoro"
    };

    public string Mode { get; init; } = "kokoro";
    public string KokoroVoice { get; init; } = "af_heart";
    public double KokoroSpeed { get; init; } = 1.0;
    public string KokoroPythonPath { get; init; } = "../../artifacts/coqui-venv/Scripts/python.exe";
    public string KokoroWorkerPath { get; init; } = "../../scripts/kokoro-worker.py";
    public int KokoroPort { get; init; } = 17835;
    public string Voice { get; init; } = string.Empty;
    public int Rate { get; init; } = 0;
    public int Volume { get; init; } = 100;
    public string CloudModel { get; init; } = "gpt-4o-mini-tts";
    public string CloudVoice { get; init; } = "alloy";
    public double CloudSpeed { get; init; } = 1.0;
    public string EdgeVoice { get; init; } = "en-US-AndrewMultilingualNeural";
    public int EdgeRatePercent { get; init; } = 0;
}
