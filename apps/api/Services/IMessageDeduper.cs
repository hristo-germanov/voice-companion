namespace ChatGPTVoiceCompanion.Services;

public interface IMessageDeduper
{
    bool ShouldSpeak(string key, string text);
    void Update(string key, string text);
}
