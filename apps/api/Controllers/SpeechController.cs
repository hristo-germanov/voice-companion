using ChatGPTVoiceCompanion.Services;
using ChatGPTVoiceCompanion.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public IActionResult Pause()
    {
        _speech.Pause();
        return Ok();
    }

    [HttpPost("resume")]
    public IActionResult Resume()
    {
        _speech.Resume();
        return Ok();
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        _speech.StopAll();
        return Ok();
    }

    [HttpPost("skip")]
    public IActionResult Skip()
    {
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
    public async Task<IActionResult> PlayMessage(int messageId, CancellationToken ct)
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

        _speech.Enqueue(messageId.ToString(), message.Text);
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
            .FirstOrDefaultAsync(ct) ?? "local";

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
