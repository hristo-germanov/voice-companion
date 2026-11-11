namespace ChatGPTVoiceCompanion.Models;

public sealed class SiteProfile
{
    public int Id { get; set; }
    public string Site { get; set; } = "chatgpt";
    public string SelectorsVersion { get; set; } = "v1";
    public bool Enabled { get; set; } = true;
}
