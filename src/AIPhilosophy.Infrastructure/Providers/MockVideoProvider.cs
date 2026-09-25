using System.Diagnostics;
using System.Text.RegularExpressions;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>
/// Mock provider. Renders a real vertical MP4 locally: draws the script onto a 1080x1920 poster
/// (same art as the SVG thumbnail), then encodes it with a slow zoom and fade via ffmpeg so
/// previews work with zero external hosts. Falls back to a demo URL if rendering is unavailable.
/// Replace by pointing GenerationProvider settings at a real provider.
/// </summary>
public class MockVideoProvider(IConfiguration config, ILogger<MockVideoProvider> logger, IHttpContextAccessor http) : IVideoProvider
{
    private readonly IConfiguration _config = config;
    private readonly ILogger<MockVideoProvider> _logger = logger;
    private readonly IHttpContextAccessor _http = http;
    private static readonly string[] Splash =
    {
        "#141E30", "#243B55", "#0F2027", "#2C5364", "#42275a", "#734b6d"
    };

    public GenerationProvider Provider => GenerationProvider.Mock;
    public bool IsConfigured => true;

    public Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken ct)
    {
        var jobId = $"mock-{Guid.NewGuid():N}";
        var webRoot = _config["Mock:OutputPath"] ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "videos");

        try
        {
            Directory.CreateDirectory(webRoot);

            var baseUrl = (_config["Mock:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var ctx = _http?.HttpContext;
                if (ctx?.Request?.Host.HasValue == true)
                    baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            }
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = "http://localhost:5128";

            var thumbUrl = $"{baseUrl}/videos/{jobId}.png";
            var videoUrl = $"{baseUrl}/videos/{jobId}.mp4";

            var duration = Math.Clamp(request.DurationSeconds > 0 ? request.DurationSeconds : 10, 8, 20);

            RenderPoster(webRoot, $"{jobId}.png", request.Title ?? "365 Days of Philosophy", Clean(request.ScriptText), request.DayNumber, PickBg(request.DayNumber), duration);

            var rendered = RenderMp4(webRoot, jobId, duration, ct);
            if (!rendered)
                videoUrl = _config["Mock:DemoVideoUrl"] ?? videoUrl;

            _logger.LogInformation("Mock video job {JobId} created -> {Video} (thumb {Thumb}, local={Local})", jobId, videoUrl, thumbUrl, rendered);
            return Task.FromResult(new VideoGenerationResult
            {
                Success = true,
                IsComplete = true,
                ProviderJobId = jobId,
                VideoUrl = videoUrl,
                ThumbnailUrl = thumbUrl,
                DurationSeconds = rendered ? duration : request.DurationSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mock video generation failed for script {ScriptId}", request.ScriptId);
            return Task.FromResult(new VideoGenerationResult { Success = false, Error = ex.Message });
        }
    }

    public Task<GenerationStatusResult> CheckStatusAsync(string providerJobId, CancellationToken ct)
        => Task.FromResult(new GenerationStatusResult { Success = true, IsComplete = true });

    private bool RenderMp4(string outDir, string jobId, int duration, CancellationToken ct)
    {
        var ffmpeg = _config["Mock:FfmpegPath"] ?? "ffmpeg";
        var mp4File = $"{jobId}.mp4";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                WorkingDirectory = outDir,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add($"{jobId}.png");
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add($"zoompan=z='min(zoom+0.0004,1.12)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d={duration * 24}:s=1080x1920:fps=24,fade=t=in:st=0:d=0.8,fade=t=out:st={duration - 1.2:0.0}:d=1.2,format=yuv420p");
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add("libx264");
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add("veryfast");
            startInfo.ArgumentList.Add("-crf");
            startInfo.ArgumentList.Add("28");
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add("24");
            startInfo.ArgumentList.Add(mp4File);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("ffmpeg could not be started for {JobId}", jobId);
                return false;
            }
            var err = process.StandardError.ReadToEnd();
            var timedOut = !process.WaitForExit(180_000);

            if (timedOut || process.ExitCode != 0)
            {
                _logger.LogWarning("ffmpeg render failed for {JobId} (exit {Exit}, timeout {TimedOut}): {Err}",
                    jobId, timedOut ? -1 : process.ExitCode, timedOut, Truncate(err, 800));
                return false;
            }

            var outPath = Path.Combine(outDir, mp4File);
            var ok = File.Exists(outPath) && new FileInfo(outPath).Length > 1_000;
            if (!ok)
                _logger.LogWarning("ffmpeg exited 0 but output file missing/tiny for {JobId}: {Err}", jobId, Truncate(err, 400));
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffmpeg render error for {JobId}", jobId);
            return false;
        }
    }

    private static string PickBg(int day)
        => Splash[day % Splash.Length];

    private static void RenderPoster(string dir, string file, string title, string quote, int day, string bg, int duration)
    {
        const int W = 1080, H = 1920;
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        using (var bgPaint = new SKPaint
               {
                   Shader = SKShader.CreateLinearGradient(
                       new SKPoint(0, 0), new SKPoint(W, H),
                       new[] { SKColor.Parse(bg), SKColor.Parse("#050A0F") },
                       new[] { 0f, 1f },
                       SKShaderTileMode.Clamp)
               })
        {
            canvas.DrawRect(0, 0, W, H, bgPaint);
        }

        using (var dayPaint = new SKPaint { Color = SKColor.Parse("#E8C47C"), IsAntialias = true })
        using (var dayFont = new SKFont(PickTypeface("Segoe UI", "DejaVu Sans", "Arial", "Helvetica"), 44))
        {
            canvas.DrawText($"DAY {day}", 150, BaselineFor(dayFont, 240, 44), SKTextAlign.Left, dayFont, dayPaint);
        }

        using (var quotePaint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        using (var quoteFont = new SKFont(PickTypeface("Segoe UI", "DejaVu Sans", "Arial", "Helvetica"), 54))
        {
            var lines = Wrap(quote, 30, 6).Split('\n');
            var lineH = 74f;
            var startY = (H - lines.Length * lineH) / 2f;
            var y = startY;
            foreach (var line in lines)
            {
                var w = quoteFont.MeasureText(line);
                canvas.DrawText(line, (W - w) / 2f, BaselineFor(quoteFont, y, lineH), SKTextAlign.Left, quoteFont, quotePaint);
                y += lineH;
            }
        }

        using (var titlePaint = new SKPaint { Color = SKColor.Parse("#B8C4D0"), IsAntialias = true })
        using (var titleFont = new SKFont(PickTypeface("Georgia", "DejaVu Serif", "Times New Roman", "Liberation Serif", "Noto Serif"), 42))
        {
            var tsize = titleFont.MeasureText(title);
            canvas.DrawText(title, Math.Max(20f, (W - tsize) / 2f), BaselineFor(titleFont, 1540, 42), SKTextAlign.Left, titleFont, titlePaint);
        }

        using (var footPaint = new SKPaint { Color = SKColor.Parse("#7F8C98"), IsAntialias = true })
        using (var footFont = new SKFont(PickTypeface("Segoe UI", "DejaVu Sans", "Arial", "Helvetica"), 30))
        {
            var footer = $"AI VIDEO - 365 Days of Philosophy - {duration}s";
            var fsize = footFont.MeasureText(footer);
            canvas.DrawText(footer, (W - fsize) / 2f, BaselineFor(footFont, 1660, 30), SKTextAlign.Left, footFont, footPaint);
        }

        canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(Path.Combine(dir, file));
        data.SaveTo(fs);
    }

    private static SKTypeface PickTypeface(params string[] families)
    {
        foreach (var family in families)
        {
            var tf = SKTypeface.FromFamilyName(family);
            if (tf != null) return tf;
        }
        return SKTypeface.Default;
    }

    private static float BaselineFor(SKFont font, float boxTop, float boxHeight)
    {
        var fm = font.Metrics;
        var textHeight = fm.Descent - fm.Ascent;
        return boxTop + (boxHeight - textHeight) / 2f - fm.Ascent;
    }

    private static List<string> WordWrap(string text, int maxChars)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var w in words)
        {
            if (current.Length + w.Length + 1 > maxChars)
            {
                if (current.Length > 0) lines.Add(current.ToString().Trim());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(w);
        }
        if (current.Length > 0) lines.Add(current.ToString().Trim());
        return lines;
    }

    private static string Wrap(string text, int maxChars, int maxLines)
        => string.Join('\n', WordWrap(text, maxChars).Take(maxLines));

    private static string Clean(string input)
        => Regex.Replace(Regex.Replace(input ?? string.Empty, @"\[.*?\]", " "), @"\s+", " ").Trim();

    private static string Truncate(string s, int len) => s.Length <= len ? s : s[..len];
}