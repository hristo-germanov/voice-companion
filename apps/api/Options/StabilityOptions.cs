namespace ChatGPTVoiceCompanion.Options;

public sealed class StabilityOptions
{
    public const string SectionName = "Stability";

    public int WindowMs { get; init; } = 1200;
    public int PollMs { get; init; } = 250;
}
