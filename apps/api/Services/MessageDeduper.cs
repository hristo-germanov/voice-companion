using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ChatGPTVoiceCompanion.Services;

public sealed class MessageDeduper : IMessageDeduper
{
    private readonly ConcurrentDictionary<string, string> _lastHashes = new();

    public bool ShouldSpeak(string key, string text)
    {
        var hash = Hash(text);
        return !_lastHashes.TryGetValue(key, out var last) || last != hash;
    }

    public void Update(string key, string text)
    {
        _lastHashes[key] = Hash(text);
    }

    private static string Hash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text.Trim()));
        return Convert.ToHexString(bytes);
    }
}
