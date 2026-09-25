using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>
/// Mock social provider. Simulates successful posting to any platform so the
/// scheduling pipeline can run end-to-end without OAuth credentials.
/// </summary>
public class MockSocialProvider(ILogger<MockSocialProvider> logger) : ISocialProvider
{
    private readonly ILogger<MockSocialProvider> _logger = logger;
    public SocialPlatform Platform => SocialPlatform.None;
    public bool IsConfigured => true;

    public Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
    {
        _logger.LogInformation("Mock validated account {Account}", account.AccountHandle);
        return Task.CompletedTask;
    }

    public Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
    {
        var fakeUrl = $"https://demo.example.com/{request.Platform.ToString().ToLower()}/{Guid.NewGuid():N}";
        _logger.LogInformation("Mock posted video to {Platform} for account {Account}", request.Platform, account.AccountHandle);
        return Task.FromResult(new PostResult
        {
            Success = true,
            PostUrl = fakeUrl
        });
    }
}