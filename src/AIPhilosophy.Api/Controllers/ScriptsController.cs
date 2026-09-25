using AIPhilosophy.Api.Models;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/scripts")]
public class ScriptsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetScripts(
        [FromQuery] int? day, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var query = db.Scripts.Where(s => s.TenantId == tenantId);

        if (day.HasValue) query = query.Where(s => s.DayNumber == day.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status.ToString() == status);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(s => s.DayNumber)
            .Skip(Math.Max(0, page - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100))
            .Select(Projection.Dto)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { total, items }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetScript(Guid id)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var script = await db.Scripts.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        return script == null ? NotFound() : Ok(ApiResponse<object>.Ok(Projection.ToDto(script)));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RequestModels.ScriptUpsertRequest req)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var script = await db.Scripts.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (script == null) return NotFound();

        script.Title = req.Title;
        script.Subtitle = req.Subtitle;
        script.Category = req.Category;
        script.Philosopher = req.Philosopher;
        script.Body = req.Body;
        script.VideoPrompt = req.VideoPrompt;
        script.Description = req.Description;
        script.Hashtags = req.Hashtags;
        script.EstimatedDurationSeconds = req.EstimatedDurationSeconds;
        script.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(req.ScheduledPublishDate) && DateTime.TryParse(req.ScheduledPublishDate, out var dt))
            script.ScheduledPublishDate = dt;

        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Projection.ToDto(script)));
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] ScriptStatusRequest req)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var script = await db.Scripts.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (script == null) return NotFound();

        if (!Enum.TryParse<ScriptStatus>(req.Status, true, out var status))
            return BadRequest(ApiResponse<object>.Fail($"Unknown status '{req.Status}'."));

        script.Status = status;
        script.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Projection.ToDto(script)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RequestModels.ScriptUpsertRequest req)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        if (await db.Scripts.AnyAsync(s => s.TenantId == tenantId && s.DayNumber == req.DayNumber))
            return Conflict(ApiResponse<object>.Fail($"Day {req.DayNumber} already exists."));

        var script = new Script
        {
            TenantId = tenantId,
            DayNumber = req.DayNumber,
            Title = req.Title,
            Subtitle = req.Subtitle,
            Category = req.Category,
            Philosopher = req.Philosopher,
            Body = req.Body,
            VideoPrompt = req.VideoPrompt,
            Description = req.Description,
            Hashtags = req.Hashtags,
            EstimatedDurationSeconds = req.EstimatedDurationSeconds,
            Status = ScriptStatus.Draft
        };

        if (!string.IsNullOrWhiteSpace(req.ScheduledPublishDate) && DateTime.TryParse(req.ScheduledPublishDate, out var dt))
            script.ScheduledPublishDate = dt;

        db.Scripts.Add(script);
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Projection.ToDto(script)));
    }

    [HttpGet("next-day")]
    public async Task<IActionResult> NextDay()
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var taken = await db.Scripts
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.DayNumber)
            .ToListAsync();
        var used = taken.ToHashSet();
        var nextDay = Enumerable.Range(1, int.MaxValue).First(n => !used.Contains(n));
        return Ok(ApiResponse<object>.Ok(new { nextDay }));
    }

    [HttpPost("custom")]
    public async Task<IActionResult> CreateCustom([FromBody] RequestModels.ScriptCustomRequest req)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        if (string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(ApiResponse<object>.Fail("Write your custom script narration before saving."));

        var taken = await db.Scripts
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.DayNumber)
            .ToListAsync();
        var used = taken.ToHashSet();
        var day = req.DayNumber ?? Enumerable.Range(1, int.MaxValue).First(n => !used.Contains(n));
        if (used.Contains(day))
            return Conflict(ApiResponse<object>.Fail($"Day {day} already exists."));

        var body = req.Body.Trim();
        var title = string.IsNullOrWhiteSpace(req.Title)
            ? AutoTitle(body, day)
            : req.Title.Trim();
        var prompt = string.IsNullOrWhiteSpace(req.VideoPrompt)
            ? $"Abstract meditative scenes evoking the theme of this reflection on {title}. Flowing light, deep blue and gold, calm elegant motion, cinematic, 4k."
            : req.VideoPrompt.Trim();
        var description = string.IsNullOrWhiteSpace(req.Description)
            ? $"{title} - A custom daily reflection for Day {day}. {Truncate(body, 400)}"
            : req.Description.Trim();
        var hashtags = string.IsNullOrWhiteSpace(req.Hashtags)
            ? "#Philosophy #DailyWisdom #AI"
            : req.Hashtags.Trim();

        var script = new Script
        {
            TenantId = tenantId,
            DayNumber = day,
            Title = title,
            Subtitle = "A custom reflection",
            Category = string.IsNullOrWhiteSpace(req.Category) ? "Custom Philosophy" : req.Category.Trim(),
            Philosopher = string.IsNullOrWhiteSpace(req.Philosopher) ? "Anonymous" : req.Philosopher.Trim(),
            Body = body,
            VideoPrompt = prompt,
            Description = description,
            Hashtags = hashtags,
            EstimatedDurationSeconds = Math.Max(30, Math.Min(180, body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 3)),
            Status = ScriptStatus.Draft
        };

        if (!string.IsNullOrWhiteSpace(req.ScheduledPublishDate) && DateTime.TryParse(req.ScheduledPublishDate, out var dt))
            script.ScheduledPublishDate = dt;

        db.Scripts.Add(script);
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(Projection.ToDto(script)));
    }

    private static string AutoTitle(string body, int day)
    {
        var first = body
            .Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? body;
        first = first.Trim().TrimEnd('.', '!', '?', ':');
        if (first.Length > 120) first = first[..117] + "...";
        return string.IsNullOrWhiteSpace(first) ? $"Reflection for Day {day}" : first;
    }

    private static string Truncate(string text, int max)
    {
        var normalized = text.Replace("\n", " ").Replace("\r", " ").Trim();
        return normalized.Length <= max ? normalized : normalized[..(max - 3)] + "...";
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] RequestModels.GenerateVideoRequest req,
        [FromServices] VideoGenerationService videoService)
    {
        try
        {
            GenerationProvider? provider = null;
            if (!string.IsNullOrWhiteSpace(req.Provider) && Enum.TryParse<GenerationProvider>(req.Provider, true, out var p))
                provider = p;

            var job = await videoService.SubmitAsync(req.ScriptId, provider);
            return Ok(ApiResponse<object>.Ok(new { jobId = job.Id, status = job.Status.ToString() }));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("generate-range")]
    public async Task<IActionResult> GenerateRange([FromBody] GenerateRangeRequest req,
        [FromServices] VideoGenerationService videoService)
    {
        if (req.Days == null || req.Days is { Length: 0 })
            return BadRequest(ApiResponse<object>.Fail("Provide days array."));

        var tenantId = await this.GetTenantIdAsync(db);
        var results = new List<object>();
        foreach (var day in req.Days.Take(5))
        {
            var script = await db.Scripts.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.DayNumber == day);
            if (script == null)
            {
                results.Add(new { day, success = false, error = "Not found." });
                continue;
            }
            try
            {
                var job = await videoService.SubmitAsync(script.Id, null);
                results.Add(new { day, success = true, jobId = job.Id, status = job.Status.ToString() });
            }
            catch (Exception ex)
            {
                results.Add(new { day, success = false, error = ex.Message });
            }
        }
        return Ok(ApiResponse<object>.Ok(results));
    }

    public record ScriptStatusRequest(string Status);
    public record GenerateRangeRequest(int[] Days);
}

public static class Projection
{
    public static System.Linq.Expressions.Expression<Func<Script, RequestModels.ScriptDto>> Dto => s =>
        new(s.Id, s.DayNumber, s.Title, s.Subtitle, s.Category, s.Philosopher,
            s.Body, s.VideoPrompt, s.Description, s.Hashtags, s.Status.ToString(),
            s.EstimatedDurationSeconds, s.ScheduledPublishDate.HasValue
                ? s.ScheduledPublishDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")
                : null);

    public static RequestModels.ScriptDto ToDto(Script s) => new(
        s.Id, s.DayNumber, s.Title, s.Subtitle, s.Category, s.Philosopher,
        s.Body, s.VideoPrompt, s.Description, s.Hashtags, s.Status.ToString(),
        s.EstimatedDurationSeconds, s.ScheduledPublishDate.HasValue
            ? s.ScheduledPublishDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")
            : null);
}
