using System.Threading.Channels;

namespace ChatGPTVoiceCompanion.Services;

internal sealed class SpeechQueueEntry(string messageId, string text, double startSeconds = 0)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string MessageId { get; } = messageId;
    public string Text { get; } = text;
    public double StartSeconds { get; } = startSeconds;
    public CancellationTokenSource Cancellation { get; } = new();
    public Task<BufferedSpeech?>? Buffer { get; set; }
}

internal sealed class SpeechWorkQueue
{
    private readonly object _gate = new();
    private readonly List<SpeechQueueEntry> _pending = [];
    private readonly Channel<SpeechQueueEntry> _channel = Channel.CreateUnbounded<SpeechQueueEntry>();
    public int Count { get { lock (_gate) return _pending.Count; } }
    public SpeechQueueEntry[] Snapshot() { lock (_gate) return _pending.ToArray(); }
    public void Add(SpeechQueueEntry item)
    {
        lock (_gate) { _pending.Add(item); _channel.Writer.TryWrite(item); }
    }
    public ValueTask<bool> WaitAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);
    public SpeechQueueEntry? Take()
    {
        lock (_gate)
        {
            while (_channel.Reader.TryRead(out var item))
                if (_pending.Remove(item)) return item;
            return null;
        }
    }
    public SpeechQueueEntry? Remove(string id)
    {
        lock (_gate)
        {
            var item = _pending.FirstOrDefault(entry => entry.Id == id);
            if (item is not null) _pending.Remove(item);
            return item;
        }
    }
    public SpeechQueueEntry[] Clear()
    {
        lock (_gate)
        {
            var result = _pending.ToArray(); _pending.Clear();
            while (_channel.Reader.TryRead(out _)) { }
            return result;
        }
    }
}
