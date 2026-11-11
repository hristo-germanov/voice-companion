namespace ChatGPTVoiceCompanion.Options;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public int Port { get; init; } = 17832;
    public string ApiKey { get; init; } = "change-me";
}
