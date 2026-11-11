using ChatGPTVoiceCompanion.Data;
using ChatGPTVoiceCompanion.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTVoiceCompanion.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var settings = await _db.AppSettings
            .OrderBy(x => x.Key)
            .ToListAsync(ct);

        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AppSetting input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Key))
        {
            return BadRequest("Key is required.");
        }

        AppSetting result;
        var existing = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == input.Key, ct);
        if (existing is null)
        {
            var created = new AppSetting
            {
                Key = input.Key,
                Value = input.Value
            };
            _db.AppSettings.Add(created);
            result = created;
        }
        else
        {
            existing.Value = input.Value;
            result = existing;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(result);
    }
}
