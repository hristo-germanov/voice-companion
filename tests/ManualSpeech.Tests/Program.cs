using ChatGPTVoiceCompanion.Controllers;
using ChatGPTVoiceCompanion.Data;
using ChatGPTVoiceCompanion.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Run with: dotnet run --project tests/ManualSpeech.Tests
// In-memory database and fake queue: never touches user data or plays audio.
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
var queue = new RecordingQueue();
var controller = new SpeechController(queue, db);

foreach (var invalid in new[] { null, "", " \n ", new string('x', 50001) })
{
    var result = await controller.ReadText(new ReadTextRequest(invalid!), default);
    Check(result is BadRequestObjectResult, "Invalid text must return 400");
}
Check(await db.CapturedMessages.CountAsync() == 0 && queue.Items.Count == 0,
    "Invalid input must not save or enqueue");

foreach (var text in new[] { "  Hello\nworld  ", new string('x', 50000), "  Hello\nworld  " })
{
    Check(await controller.ReadText(new ReadTextRequest(text), default) is OkObjectResult,
        "Valid text must be accepted, including repeated text");
}
var messages = await db.CapturedMessages.OrderBy(x => x.Id).ToListAsync();
Check(messages.Count == 3 && queue.Items.Count == 3, "Each submission must save and queue once");
Check(messages[0].Text == "Hello\nworld" && messages[0].Role == "manual", "Trim and label manual entries");
Check(queue.Items[0] == (messages[0].Id.ToString(), messages[0].Text), "Queue stored ID and text");
Check(messages[0].TextHash == messages[2].TextHash, "Hash trimmed text consistently");
Check(await controller.PlayMessage(messages[0].Id, default) is OkObjectResult, "Manual history must replay");
Check(queue.Items.Count == 4, "Replay must enqueue");
Console.WriteLine("Manual speech regression checks passed.");
var historyController = new HistoryController(db, queue);
db.SpeechEvents.AddRange(
    new ChatGPTVoiceCompanion.Models.SpeechEvent { MessageId = messages[0].Id },
    new ChatGPTVoiceCompanion.Models.SpeechEvent { MessageId = messages[1].Id });
await db.SaveChangesAsync();
Check(await historyController.Delete(messages[0].Id) is NoContentResult, "Delete saved text");
Check(!await db.CapturedMessages.AnyAsync(x => x.Id == messages[0].Id), "Deleted text stays out of history");
Check(!queue.Items.Any(x => x.Id == messages[0].Id.ToString()), "Remove every queued copy");
Check(!await db.SpeechEvents.AnyAsync(x => x.MessageId == messages[0].Id), "Remove associated speech events");
Check(await db.CapturedMessages.CountAsync() == 2 && await db.SpeechEvents.CountAsync() == 1, "Preserve unrelated texts and events");
Check(await historyController.Delete(messages[0].Id) is NotFoundResult, "Missing text returns 404");
Console.WriteLine("History deletion regression checks passed.");

Check(EdgeSpeechText.Split("Hello.\r\n```\n---\n##\nWorld!").SequenceEqual(new[] { "Hello.\nWorld!" }),
    "Markdown separators must not become speech requests");
Check(EdgeSpeechText.Split("```\n!?\n---").Count == 0, "Symbol-only text must not reach Edge");
Check(EdgeSpeechText.Split("Здравей!\n你好。\n123").Single() == "Здравей!\n你好。\n123", "Keep Unicode text and numbers");
var paragraph = "First sentence.\n\nSecond sentence! What comes next? Version 1.2 stays intact.";
Check(EdgeSpeechText.Split(paragraph).SequenceEqual(new[] { paragraph }),
    "Sentences and paragraphs must use one audio request with original punctuation");
var longText = string.Concat(Enumerable.Repeat("A sentence stays intact. Next sentence follows! ", 500));
var chunks = EdgeSpeechText.Split(longText);
Check(chunks.Count > 1 && chunks[0].Length <= 400 && chunks.All(chunk => chunk.Length <= 1000),
    "Long text must start with a small chunk and use bounded later chunks");
Check(string.Concat(chunks) == longText.Trim(), "Chunking must not lose or repeat characters");
Check(string.Concat(EdgeSpeechText.Split(new string('a', 50000))) == new string('a', 50000),
    "Unbroken long text must not be lost");
Console.WriteLine("Edge text segmentation regression checks passed.");
Check(new ChatGPTVoiceCompanion.Options.SpeechOptions().Mode == "kokoro", "Default engine must be Kokoro");
Check(new ChatGPTVoiceCompanion.Options.SpeechOptions().KokoroVoice == "af_heart", "Default voice must be Heart");
foreach (var mode in new string?[] { null, "", "edge", "kokoro", "unknown" })
    Check(ChatGPTVoiceCompanion.Options.SpeechOptions.NormalizeMode(mode) == "kokoro", "Legacy and missing engines must use Kokoro");
Check(ChatGPTVoiceCompanion.Options.SpeechOptions.NormalizeMode("local") == "local", "Preserve Windows mode");
Check(ChatGPTVoiceCompanion.Options.SpeechOptions.NormalizeMode("cloud") == "cloud", "Preserve cloud mode");
Console.WriteLine("Kokoro default and legacy-mode regression checks passed.");

var work = new SpeechWorkQueue();
var firstQueued = new SpeechQueueEntry("1", "First");
var removedQueued = new SpeechQueueEntry("2", "Remove me");
var lastQueued = new SpeechQueueEntry("3", "Last");
work.Add(firstQueued); work.Add(removedQueued); work.Add(lastQueued);
Check(work.Remove(removedQueued.Id) == removedQueued, "Remove only the selected waiting entry");
Check(work.Take() == firstQueued && work.Take() == lastQueued && work.Take() is null, "Preserve FIFO order and never read removed entries");
work.Add(new SpeechQueueEntry("4", "Clear"));
Check(work.Clear().Length == 1 && work.Count == 0 && work.Take() is null, "Clear the waiting queue");

var sourceText = "😀 Header.\r\n" + string.Concat(Enumerable.Repeat("This sentence preserves every original offset. ", 70));
var originalParts = SpeechTextParts.Split(sourceText);
Check(string.Concat(originalParts.Select(part => part.Text)) == sourceText, "Keep original text for exact word offsets");
Check(originalParts.All(part => sourceText.Substring(part.Start, part.Text.Length) == part.Text), "Chunks use original UTF-16 offsets");
string bufferDirectory;
await using (var buffered = new BufferedSpeech(sourceText, "af_heart", 1, (part, path, token) =>
    Task.FromResult(new BufferedAudioChunk(path, 10, [new SpeechWordTiming(0, 4, 0, 1)])), default))
{
    await buffered.Completion;
    bufferDirectory = buffered.DirectoryPath;
    Check(buffered.IsComplete && buffered.ReadyCount == originalParts.Count, "All audio parts become ready");
    Check(buffered.Locate(15) == (1, 5.0), "Seek across audio part boundaries");
    Check(buffered.Locate(-15) == (0, 0.0), "Clamp rewind at beginning");
    Check(buffered.Locate(99999).Index == originalParts.Count - 1, "Clamp forward seek to buffered audio");
}
Check(!Directory.Exists(bufferDirectory), "Clean up buffered audio after disposal");
var canceled = new BufferedSpeech("Cancel queued audio", "af_heart", 1, async (part, path, token) =>
{
    await Task.Delay(Timeout.Infinite, token);
    return new BufferedAudioChunk(path, 1, []);
}, default);
await canceled.DisposeAsync();
Check(canceled.Completion.IsCompleted && !Directory.Exists(canceled.DirectoryPath), "Cancellation stops queued buffering and cleans files");

using (var legacyConnection = new SqliteConnection("Data Source=:memory:"))
{
    legacyConnection.Open();
    await using var legacyDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(legacyConnection).Options);
    await legacyDb.Database.ExecuteSqlRawAsync("CREATE TABLE CapturedMessages (Id INTEGER PRIMARY KEY, Text TEXT, SpokeAt TEXT)");
    await legacyDb.Database.ExecuteSqlRawAsync("INSERT INTO CapturedMessages VALUES (1, 'Keep this text', '2026-11-11')");
    PlaybackProgressSchema.Ensure(legacyDb);
    await legacyDb.Database.ExecuteSqlRawAsync("UPDATE CapturedMessages SET PlaybackCompleted = 0, PlaybackProgressPercent = 37, PlaybackPositionSeconds = 12");
    PlaybackProgressSchema.Ensure(legacyDb);
    using var command = legacyConnection.CreateCommand();
    command.CommandText = "SELECT Text, PlaybackProgressPercent, PlaybackPositionSeconds, PlaybackCompleted FROM CapturedMessages";
    using var reader = command.ExecuteReader();
    Check(reader.Read() && reader.GetString(0) == "Keep this text" && reader.GetInt32(1) == 37 && reader.GetDouble(2) == 12 && !reader.GetBoolean(3),
        "Schema upgrade preserves text and saved progress on repeated startup");
}
Console.WriteLine("Queue, buffering, seek and progress migration regression checks passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class RecordingQueue : ISpeechQueue
{
    public List<(string Id, string Text)> Items { get; } = new();
    public void Enqueue(string messageId, string text, double startSeconds = 0) => Items.Add((messageId, text));
    public void Pause() { }
    public void Resume() { }
    public void StopAll() { }
    public void SkipCurrent() { }
    public void RepeatLast() { }
    public SpeechTrackerSnapshot GetTracker() => throw new NotSupportedException();
    public IReadOnlyList<QueuedSpeechSnapshot> GetPending() => [];
    public bool RemovePending(string id) => false;
    public void ClearPending() { }
    public Task RemoveMessageAsync(string messageId) { Items.RemoveAll(x => x.Id == messageId); return Task.CompletedTask; }
    public void Seek(double seconds) { }
    public void SeekRelative(double seconds) { }
    public Task SaveProgressAsync(CancellationToken ct = default) => Task.CompletedTask;
}
