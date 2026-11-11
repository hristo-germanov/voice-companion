namespace ChatGPTVoiceCompanion.Services;

public interface IStabilityBuffer
{
    void Update(string key, PendingMessage message);
}

public sealed record PendingMessage(string MessageId, string Role, string Text);
