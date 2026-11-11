namespace ChatGPTVoiceCompanion.Options;

public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    public string Mode { get; init; } = "edge";
    public string Voice { get; init; } = string.Empty;
    public int Rate { get; init; } = 0;
    public int Volume { get; init; } = 100;
    public string CloudModel { get; init; } = "gpt-4o-mini-tts";
    public string CloudVoice { get; init; } = "alloy";
    public double CloudSpeed { get; init; } = 1.0;
    public string EdgeVoice { get; init; } = "en-US-AndrewMultilingualNeural";
    public int EdgeRatePercent { get; init; } = 0;
}
