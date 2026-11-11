using ChatGPTVoiceCompanion.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTVoiceCompanion.Controllers;

[ApiController]
[Route("api/history")]
public sealed class HistoryController : ControllerBase
{
    private readonly AppDbContext _db;

    public HistoryController(AppDbContext db)
    {
        _db = db;
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
                x.SpokeAt
            })
            .ToListAsync(ct);

        return Ok(items);
    }
}
