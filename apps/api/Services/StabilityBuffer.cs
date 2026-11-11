using System.Collections.Concurrent;
using ChatGPTVoiceCompanion.Options;
using Microsoft.Extensions.Options;

namespace ChatGPTVoiceCompanion.Services;

public sealed class StabilityBuffer : BackgroundService, IStabilityBuffer
{
    private readonly ConcurrentDictionary<string, PendingState> _pending = new();
    private readonly ISpeechQueue _speech;
    private readonly StabilityOptions _options;

    public StabilityBuffer(ISpeechQueue speech, IOptions<StabilityOptions> options)
    {
        _speech = speech;
        _options = options.Value;
    }

    public void Update(string key, PendingMessage message)
    {
        _pending[key] = new PendingState(message, DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollDelay = TimeSpan.FromMilliseconds(Math.Max(100, _options.PollMs));
        var window = TimeSpan.FromMilliseconds(Math.Max(300, _options.WindowMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _pending.ToArray())
            {
                if (now - entry.Value.UpdatedAt < window)
                {
                    continue;
                }

                if (_pending.TryRemove(entry.Key, out var state))
                {
                    if (state.Message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        _speech.Enqueue(state.Message.MessageId, state.Message.Text);
                    }
                }
            }

            await Task.Delay(pollDelay, stoppingToken);
        }
    }

    private sealed record PendingState(PendingMessage Message, DateTimeOffset UpdatedAt);
}
