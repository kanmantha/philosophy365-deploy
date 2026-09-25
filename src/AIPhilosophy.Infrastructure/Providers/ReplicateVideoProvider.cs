using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>
/// Real AI video generation via Replicate (https://replicate.com).
/// Configure: Replicate:ApiToken. Model defaults to "stability-ai/stable-video-diffusion".
/// Swap the model/inputs for any Replicate model (Wan, Pika, Kling, Hunyuan et al.).
/// </summary>
public class ReplicateVideoProvider(HttpClient http, IConfiguration config, ILogger<ReplicateVideoProvider> logger)
    : IVideoProvider
{
    private readonly IConfiguration _config = config;
    private readonly ILogger<ReplicateVideoProvider> _logger = logger;
    private readonly HttpClient _http = http;

    public GenerationProvider Provider => GenerationProvider.Replicate;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["Replicate:ApiToken"]);

    public async Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken ct)
    {
        var token = _config["Replicate:ApiToken"] ?? "";
        var model = _config["Replicate:Model"] ?? "stability-ai/stable-video-diffusion:3f0457e4619daac51203dedb472816fd4af51f3149fa7a9e0b5ffcf1b8172438";
        var modelId = model;
        var version = (string?)null;
        var split = model.Split(':');
        if (split.Length == 2)
        {
            modelId = split[0];
            version = split[1];
        }

        var payload = new
        {
            input = new
            {
                prompt = BuildPrompt(request.VideoPrompt, request.ScriptText, request.Title),
                negative_prompt = "text, watermark, logo, low quality, blurry, distorted",
                num_frames = EstimateFrames(request.DurationSeconds),
                fps = 24,
                seed = Random.Shared.Next(0, int.MaxValue),
                guidance_scale = 3,
                output_format = "mp4"
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/predictions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Replicate submission failed: {Status} {Body}", resp.StatusCode, body);
            return new VideoGenerationResult { Success = false, Error = $"Replicate: {resp.StatusCode} {body}" };
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var jobId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        return new VideoGenerationResult { Success = true, ProviderJobId = jobId };
    }

    public async Task<GenerationStatusResult> CheckStatusAsync(string providerJobId, CancellationToken ct)
    {
        var token = _config["Replicate:ApiToken"] ?? "";
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.replicate.com/v1/predictions/{providerJobId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Replicate status check failed: {Status} {Body}", resp.StatusCode, body);
            return new GenerationStatusResult { Success = false, Error = $"Replicate: {resp.StatusCode}" };
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;

        if (status == "succeeded")
        {
            string? videoUrl = null;
            string? thumbUrl = null;
            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String)
                videoUrl = output.GetString();
            else if (root.TryGetProperty("output", out output) && output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
            {
                var first = output[0];
                videoUrl = first.ValueKind == JsonValueKind.String ? first.GetString() : first.GetProperty("url").GetString();
                if (output.GetArrayLength() > 1)
                {
                    var second = output[1];
                    thumbUrl = second.ValueKind == JsonValueKind.String ? second.GetString() : second.GetProperty("url").GetString();
                }
            }
            return new GenerationStatusResult { Success = true, IsComplete = true, VideoUrl = videoUrl, ThumbnailUrl = thumbUrl };
        }

        if (status == "failed" || status == "canceled")
        {
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : "Generation failed";
            return new GenerationStatusResult { Success = false, Error = error };
        }

        return new GenerationStatusResult { Success = true, IsComplete = false };
    }

    private static string BuildPrompt(string videoPrompt, string script, string title)
        => $"{videoPrompt}. Cinematic, high detail, serene atmosphere inspired by: {title}. Narration theme: {Truncate(script, 400)}";

    private static string Truncate(string s, int len)
        => s.Length <= len ? s : s[..len];

    private static int EstimateFrames(int seconds)
        => Math.Clamp(seconds * 24, 24, 128);
}