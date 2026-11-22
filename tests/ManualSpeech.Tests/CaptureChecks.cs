using System.Diagnostics;
using ChatGPTVoiceCompanion.Controllers;
using ChatGPTVoiceCompanion.Data;
using ChatGPTVoiceCompanion.Models;
using ChatGPTVoiceCompanion.Options;
using ChatGPTVoiceCompanion.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

internal static class CaptureChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var pending = new RecordingStabilityBuffer();
        var capture = new CaptureController(db, new MessageDeduper(), Options.Create(new ApiOptions { ApiKey = "test-key" }), pending)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var reply = new CaptureEventDto("assistant", "  A partial reply. ", null, "reply-1", "conversation-1", "chatgpt");
        var prompt = reply with { Role = "user", Text = "A question?", SourceMessageId = "prompt-1" };

        check(await capture.CaptureEvent(reply, default) is UnauthorizedResult, "Require the capture token");
        capture.Request.Headers["X-Reader-Token"] = "test-key";
        check(await capture.CaptureEvent(reply with { Text = " \n " }, default) is BadRequestObjectResult, "Reject blank capture");
        check(await db.CapturedMessages.CountAsync() == 0 && pending.Updates.Count == 0,
            "Rejected captures cannot create history or pending speech");

        check(await capture.CaptureEvent(reply, default) is OkObjectResult, "Accept captured ChatGPT replies");
        var saved = await db.CapturedMessages.SingleAsync();
        check(saved.Text == "A partial reply." && saved.TextHash.Length == 64
            && pending.Updates.Single() == ("conversation-1:reply-1", new PendingMessage(saved.Id.ToString(), "assistant", saved.Text)),
            "Save trimmed text and queue new assistant snapshots by conversation and source");

        await capture.CaptureEvent(reply with { Text = "A partial reply." }, default);
        check(pending.Updates.Count == 1, "Suppress unchanged replies even when only surrounding whitespace changes");
        var finishedReply = reply with { Text = "The completed reply." };
        await capture.CaptureEvent(finishedReply, default);
        check(await db.CapturedMessages.CountAsync() == 1 && saved.Text == finishedReply.Text
            && pending.Updates[^1].Message.Text == finishedReply.Text && pending.Updates.Count == 2,
            "Persist streaming edits under the same history ID and update pending speech");

        await capture.CaptureEvent(prompt, default);
        check(await db.CapturedMessages.CountAsync() == 2 && pending.Updates.Count == 2,
            "Capture prompt history independently of the default automatic-reading preference");

        var settings = new SettingsController(db);
        check(await settings.Put(new AppSetting { Key = " " }, default) is BadRequestObjectResult, "Reject blank setting keys");
        check(await settings.Put(new AppSetting { Key = "speech.read_assistant", Value = "false" }, default) is OkObjectResult,
            "Save the automatic-reading preference");
        await capture.CaptureEvent(reply with { Text = "An update after disabling automatic reading." }, default);
        check(pending.Updates.Count == 2, "Disabled assistant reading still saves updates without requesting playback");
        await settings.Put(new AppSetting { Key = "speech.read_assistant", Value = "true" }, default);
        var result = (OkObjectResult)await settings.GetAll(default);
        var savedSetting = ((List<AppSetting>)result.Value!).Single();
        check(savedSetting.Key == "speech.read_assistant" && savedSetting.Value == "true", "Read back the updated setting without creating a duplicate");
        await capture.CaptureEvent(reply with { Text = "An update after enabling automatic reading." }, default);
        check(pending.Updates.Count == 3, "Enabling assistant reading resumes playback requests for new text");

        var queue = new RecordingQueue();
        const int quietWindow = 300;
        using var buffer = new StabilityBuffer(queue, Options.Create(new StabilityOptions { WindowMs = quietWindow, PollMs = 100 }));
        buffer.Update(pending.Updates[0].Key, pending.Updates[0].Message);
        var elapsed = Stopwatch.StartNew();
        buffer.Update(pending.Updates[1].Key, pending.Updates[1].Message);
        await buffer.StartAsync(default);
        try
        {
            var spoken = await queue.FirstItem.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(elapsed.ElapsedMilliseconds >= quietWindow && spoken == (saved.Id.ToString(), finishedReply.Text, 0),
                "Automatic playback waits for the quiet window and enqueues only the latest reply");
        }
        finally
        {
            await buffer.StopAsync(default);
        }
        check(queue.Items.Count == 1, "One stable reply produces one queue entry");
        Console.WriteLine("Capture, automatic reading and settings regression checks passed.");
    }

    private sealed class RecordingStabilityBuffer : IStabilityBuffer
    {
        public List<(string Key, PendingMessage Message)> Updates { get; } = new();
        public void Update(string key, PendingMessage message) => Updates.Add((key, message));
    }
}
