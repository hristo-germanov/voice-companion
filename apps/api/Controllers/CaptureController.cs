using ChatGPTVoiceCompanion.Data;
using ChatGPTVoiceCompanion.Models;
using ChatGPTVoiceCompanion.Options;
using ChatGPTVoiceCompanion.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace ChatGPTVoiceCompanion.Controllers;

[ApiController]
[Route("api/capture")]
public sealed class CaptureController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMessageDeduper _deduper;
    private readonly ApiOptions _options;
    private readonly IStabilityBuffer _stability;

    public CaptureController(
        AppDbContext db,
        IMessageDeduper deduper,
        IOptions<ApiOptions> options,
        IStabilityBuffer stability)
    {
        _db = db;
        _deduper = deduper;
        _options = options.Value;
        _stability = stability;
    }

    [HttpPost("event")]
    public async Task<IActionResult> CaptureEvent([FromBody] CaptureEventDto dto, CancellationToken ct)
    {
        var token = Request.Headers["X-Reader-Token"].ToString();
        if (!string.Equals(token, _options.ApiKey, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(dto.Text))
        {
            return BadRequest("Text is required.");
        }

        var normalizedText = dto.Text.Trim();
        var textHash = string.IsNullOrWhiteSpace(dto.TextHash) ? Hash(normalizedText) : dto.TextHash;

        var session = await _db.ConversationSessions
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            session = new ConversationSession
            {
                Site = dto.Site ?? "chatgpt"
            };
            _db.ConversationSessions.Add(session);
            await _db.SaveChangesAsync(ct);
        }

        var key = $"{dto.ConversationId ?? "default"}:{dto.SourceMessageId ?? dto.Role}";
        var shouldSpeak = _deduper.ShouldSpeak(key, normalizedText);
        _deduper.Update(key, normalizedText);

        var message = await _db.CapturedMessages
            .FirstOrDefaultAsync(m => m.SourceMessageId == dto.SourceMessageId && m.SessionId == session.Id, ct);

        if (message is null)
        {
            message = new CapturedMessage
            {
                SessionId = session.Id,
                Role = dto.Role,
                Text = normalizedText,
                TextHash = textHash,
                SourceMessageId = dto.SourceMessageId
            };
            _db.CapturedMessages.Add(message);
        }
        else
        {
            message.Text = normalizedText;
            message.TextHash = textHash;
            message.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        if (shouldSpeak && await IsRoleEnabled(dto.Role, ct))
        {
            _stability.Update(key, new PendingMessage(message.Id.ToString(), dto.Role, normalizedText));
        }

        return Ok(new { messageId = message.Id, shouldSpeak });
    }

    private static string Hash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private async Task<bool> IsRoleEnabled(string role, CancellationToken ct)
    {
        var normalizedRole = role.Trim().ToLowerInvariant();
        if (normalizedRole is not ("assistant" or "user"))
        {
            return false;
        }

        var key = normalizedRole == "assistant" ? "speech.read_assistant" : "speech.read_user";
        var raw = await _db.AppSettings
            .AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return normalizedRole == "assistant";
        }

        return bool.TryParse(raw, out var parsed)
            ? parsed
            : normalizedRole == "assistant";
    }
}

public sealed record CaptureEventDto(
    string Role,
    string Text,
    string? TextHash,
    string? SourceMessageId,
    string? ConversationId,
    string? Site
);
