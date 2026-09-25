using AIPhilosophy.Api.Models;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/posts")]
public class PostsController(AppDbContext db, SocialPosterService posterService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPosts([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var query = db.Posts
            .Include(p => p.SocialAccount)
            .Include(p => p.Script)
            .Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => p.Status.ToString() == status);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(Math.Max(0, page - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100))
            .Select(p => new
            {
p.Id,
                platform = p.Platform.ToString(),
                status = p.Status.ToString(),
                accountHandle = p.SocialAccount != null ? p.SocialAccount.AccountHandle : string.Empty,
                day = p.Script != null ? p.Script.DayNumber : 0,
                title = p.Script != null ? p.Script.Title : string.Empty,
                p.Caption,
                p.PostUrl,
                p.ErrorMessage,
                p.ScheduledAt,
                p.PostedAt,
                p.RetryCount
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { total, items }));
    }

    [HttpPost("publish-due")]
    public async Task<IActionResult> PublishDue()
    {
        var published = await posterService.PublishDueAsync();
        return Ok(ApiResponse<object>.Ok(new { published }));
    }

    [HttpPost("retry-failed")]
    public async Task<IActionResult> RetryFailed()
    {
        var requeued = await posterService.RetryFailedAsync();
        return Ok(ApiResponse<object>.Ok(new { requeued }));
    }

    [HttpPost]
    public async Task<IActionResult> QueuePost([FromBody] RequestModels.QueuePostRequest req)
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
