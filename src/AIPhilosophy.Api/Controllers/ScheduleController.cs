using AIPhilosophy.Api.Models;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/schedule")]
public class ScheduleController(AppDbContext db, ICronService cronService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRules()
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var rules = await db.ScheduleRules
            .Where(r => r.TenantId == tenantId)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.IsEnabled,
                r.GenerationHourUTC,
                r.PostHourUTC,
                r.PostingDaysCsv,
                r.PostingTimesCsv,
                r.TargetPlatformsCsv,
                r.TimeZoneId,
                generationCron = cronService.BuildDailyCron(r.GenerationHourUTC, 0, new List<string>(r.PostingDaysCsv.Split(',')))
            })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(rules));
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] RequestModels.ScheduleRuleRequest req)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var rule = await db.ScheduleRules.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == req.Name);

        if (rule == null)
        {
            rule = new ScheduleRule { TenantId = tenantId, Name = req.Name };
            db.ScheduleRules.Add(rule);
        }

        rule.GenerationHourUTC = Math.Clamp(req.GenerationHourUTC, 0, 23);
        rule.PostHourUTC = Math.Clamp(req.PostHourUTC, 0, 23);
        rule.PostingDaysCsv = Sanitize(req.PostingDaysCsv);
        rule.PostingTimesCsv = Sanitize(req.PostingTimesCsv);
        rule.TargetPlatformsCsv = Sanitize(req.TargetPlatformsCsv);
        rule.TimeZoneId = !string.IsNullOrWhiteSpace(req.TimeZoneId) ? req.TimeZoneId : "UTC";
        rule.IsEnabled = req.IsEnabled;
        rule.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { rule.Id }, "Schedule rule saved."));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var rule = await db.ScheduleRules.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);
        if (rule == null) return NotFound();
        db.ScheduleRules.Remove(rule);
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(true));
    }

    [HttpGet("preview")]
    public IActionResult Preview([FromQuery] int generationHour = 6, [FromQuery] string days = "Mon,Tue,Wed,Thu,Fri,Sat,Sun")
    {
        var cron = cronService.BuildDailyCron(generationHour, 0, days.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());
        var times = cronService.ParsePostingTimes("09:00,12:00,18:00");
        return Ok(ApiResponse<object>.Ok(new { cron, sampleTimes = times }));
    }

    private static string Sanitize(string csv)
        => string.IsNullOrWhiteSpace(csv) ? string.Empty : string.Join(",", csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
}
