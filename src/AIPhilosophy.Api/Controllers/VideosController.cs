using AIPhilosophy.Api.Models;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/videos")]
public class VideosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetVideos([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var query = db.VideoJobs
            .Include(v => v.Script)
            .Where(v => v.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(v => v.Status.ToString() == status);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(v => v.CreatedAt)
            .Skip(Math.Max(0, page - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100))
            .Select(v => new
            {
                v.Id,
v.ScriptId,
                day = v.Script != null ? v.Script.DayNumber : 0,
                title = v.Script != null ? v.Script.Title : string.Empty,
                provider = v.Provider.ToString(),
                status = v.Status.ToString(),
                v.VideoUrl,
                v.ThumbnailUrl,
                v.DurationSeconds,
                v.RetryCount,
                v.ErrorMessage,
                v.CreatedAt,
                v.CompletedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { total, items }));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var counts = await db.VideoJobs
            .Where(v => v.TenantId == tenantId)
            .GroupBy(v => v.Status)
            .Select(g => new { status = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

        var total = counts.Sum(c => c.count);
        var generated = counts.FirstOrDefault(c => c.status == nameof(Core.Domain.VideoStatus.Ready))?.count ?? 0;
        var failed = counts.FirstOrDefault(c => c.status == nameof(Core.Domain.VideoStatus.Failed))?.count ?? 0;
        var processing = counts.FirstOrDefault(c => c.status == nameof(Core.Domain.VideoStatus.Processing))?.count ?? 0;

        return Ok(ApiResponse<object>.Ok(new { total, generated, failed, processing, byStatus = counts }));
    }

    [HttpPost("generate")]
public async Task<IActionResult> Generate([FromBody] RequestModels.GenerateVideoRequest req,
        [FromServices] VideoGenerationService videoService)
    {
        try
        {
            Core.Domain.GenerationProvider? provider = null;
            if (!string.IsNullOrWhiteSpace(req.Provider) && Enum.TryParse<Core.Domain.GenerationProvider>(req.Provider, true, out var p))
                provider = p;

            var job = await videoService.SubmitAsync(req.ScriptId, provider);
            return Ok(ApiResponse<object>.Ok(new { jobId = job.Id, status = job.Status.ToString() }));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }

    [HttpPost("poll")]
    public async Task<IActionResult> Poll([FromServices] VideoGenerationService videoService)
    {
        var completed = await videoService.PollPendingAsync();
        return Ok(ApiResponse<object>.Ok(new { completed }));
    }

    [HttpPost("queue-post")]
    public async Task<IActionResult> QueuePost([FromBody] RequestModels.QueuePostRequest req,
        [FromServices] SocialPosterService posterService)
    {
        try
        {
            if (!Enum.TryParse<Core.Domain.SocialPlatform>(req.Platform, true, out var platform))
                return BadRequest(ApiResponse<object>.Fail($"Unknown platform '{req.Platform}'."));

            DateTime? scheduled = null;
            if (!string.IsNullOrWhiteSpace(req.ScheduledAt) && DateTime.TryParse(req.ScheduledAt, out var dt))
                scheduled = dt;

            var tenantId = await this.GetTenantIdAsync(db);
            var video = await db.VideoJobs.FirstOrDefaultAsync(v => v.Id == req.VideoJobId && v.TenantId == tenantId);
            if (video == null) return NotFound();

            var post = await posterService.QueueAsync(req.VideoJobId, platform, scheduled);
            return Ok(ApiResponse<object>.Ok(new { postId = post.Id, status = post.Status.ToString() }));
        }
        catch (Exception ex)
        {
return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }
}
