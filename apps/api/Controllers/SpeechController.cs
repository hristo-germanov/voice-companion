using ChatGPTVoiceCompanion.Services;
using ChatGPTVoiceCompanion.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChatGPTVoiceCompanion.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace ChatGPTVoiceCompanion.Controllers;

[ApiController]
[Route("api/speech")]
public sealed class SpeechController : ControllerBase
{
    private readonly ISpeechQueue _speech;
    private readonly AppDbContext _db;

    public SpeechController(ISpeechQueue speech, AppDbContext db)
    {
        _speech = speech;
        _db = db;
    }

    [HttpPost("pause")]
    public async Task<IActionResult> Pause(CancellationToken ct)
    {
        _speech.Pause();
        await _speech.SaveProgressAsync(ct);
        return Ok();
    }

    [HttpGet("queue")]
    public IActionResult Queue() => Ok(_speech.GetPending());

    [HttpDelete("queue/{id}")]
    public IActionResult RemoveQueued(string id) => _speech.RemovePending(id) ? Ok() : NotFound();

    [HttpDelete("queue")]
    public IActionResult ClearQueued() { _speech.ClearPending(); return Ok(); }

    [HttpPost("seek")]
    public async Task<IActionResult> Seek([FromBody] SeekRequest request, CancellationToken ct)
    {
        if (!double.IsFinite(request.Seconds)) return BadRequest();
        if (request.Relative) _speech.SeekRelative(request.Seconds);
        else _speech.Seek(request.Seconds);
        await _speech.SaveProgressAsync(ct);
        return Ok(_speech.GetTracker());
    }

    [HttpPost("text")]
    public async Task<IActionResult> ReadText([FromBody] ReadTextRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 50000)
        {
            return BadRequest(new { error = "invalid_text", message = "Enter between 1 and 50,000 characters." });
        }

        var text = request.Text.Trim();
        // Manual entries have no browser conversation (SessionId remains zero).
        var message = new CapturedMessage
        {
            Role = "manual",
            Text = text,
            TextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        };
        _db.CapturedMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        _speech.Enqueue(message.Id.ToString(), text);
        return Ok(new { queued = true, messageId = message.Id });
    }

    [HttpPost("resume")]
    public IActionResult Resume()
    {
        _speech.Resume();
        return Ok();
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        await _speech.SaveProgressAsync(ct);
        _speech.StopAll();
        return Ok();
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip(CancellationToken ct)
    {
        await _speech.SaveProgressAsync(ct);
        _speech.SkipCurrent();
        return Ok();
    }

    [HttpPost("repeat")]
    public IActionResult Repeat()
    {
        _speech.RepeatLast();
        return Ok();
    }

    [HttpPost("play/{messageId:int}")]
    public async Task<IActionResult> PlayMessage(int messageId, CancellationToken ct, [FromQuery] bool restart = false)
    {
        var message = await _db.CapturedMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == messageId, ct);

        if (message is null)
        {
            return NotFound(new { error = "message_not_found", messageId });
        }

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return BadRequest(new { error = "message_text_empty", messageId });
        }

        _speech.Enqueue(messageId.ToString(), message.Text, restart || message.PlaybackCompleted ? 0 : message.PlaybackPositionSeconds);
        return Ok(new { queued = true, messageId });
    }

    [HttpGet("diagnostics")]
    public async Task<IActionResult> Diagnostics(CancellationToken ct)
    {
        var latest = await _db.SpeechEvents
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.MessageId,
                x.Status,
                x.StartedAt,
                x.EndedAt,
                x.Error
            })
            .FirstOrDefaultAsync(ct);

        var mode = await _db.AppSettings
            .AsNoTracking()
            .Where(x => x.Key == "speech.mode")
            .Select(x => x.Value)
            .FirstOrDefaultAsync(ct) ?? "kokoro";
        if (mode == "edge") mode = "kokoro";

        return Ok(new
        {
            mode,
            latestEvent = latest
        });
    }

    [HttpPost("diagnostics/clear-latest-error")]
    public async Task<IActionResult> ClearLatestError(CancellationToken ct)
    {
        var latest = await _db.SpeechEvents
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (latest is null)
        {
            return Ok(new { cleared = false, reason = "no_events" });
        }

        latest.Error = null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { cleared = true, eventId = latest.Id });
    }

    [HttpGet("tracker")]
    public IActionResult Tracker()
    {
        var tracker = _speech.GetTracker();
        return Ok(tracker);
    }
}

public sealed record ReadTextRequest([Required, StringLength(50000)] string Text);
public sealed record SeekRequest(double Seconds, bool Relative = false);
