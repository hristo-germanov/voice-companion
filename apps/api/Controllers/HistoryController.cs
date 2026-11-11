using ChatGPTVoiceCompanion.Services;
using ChatGPTVoiceCompanion.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTVoiceCompanion.Controllers;

[ApiController]
[Route("api/history")]
public sealed class HistoryController : ControllerBase
{
    private readonly AppDbContext _db;

    private readonly ISpeechQueue _speech;

    public HistoryController(AppDbContext db, ISpeechQueue speech)
    {
        _db = db;
        _speech = speech;
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        if (!await _db.CapturedMessages.AnyAsync(x => x.Id == id, ct)) return NotFound();
        await _speech.RemoveMessageAsync(id.ToString());
        // Once playback has been canceled, finish deletion even if the client disconnects.
        await using var transaction = await _db.Database.BeginTransactionAsync(CancellationToken.None);
        await _db.SpeechEvents.Where(x => x.MessageId == id).ExecuteDeleteAsync(CancellationToken.None);
        await _db.CapturedMessages.Where(x => x.Id == id).ExecuteDeleteAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);

        var items = await _db.CapturedMessages
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(safeLimit)
            .Select(x => new
            {
                x.Id,
                x.Role,
                x.Text,
                x.SourceMessageId,
                x.CreatedAt,
                x.UpdatedAt,
                x.SpokeAt,
                x.PlaybackPositionSeconds,
                x.PlaybackDurationSeconds,
                x.PlaybackProgressPercent,
                x.PlaybackCompleted
            })
            .ToListAsync(ct);

        return Ok(items);
    }
}
