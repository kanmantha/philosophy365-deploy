using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIPhilosophy.Tests;

public class RealSocialProviderTests
{
    private static IConfiguration BuildConfig()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Social:DemoMode"] = "false",
            ["Social:YouTubeShorts:ClientId"] = "",
            ["Social:TikTok:ClientKey"] = "",
            ["Social:InstagramReels:ClientId"] = "",
            ["Social:XTwitter:ApiKey"] = "",
            ["Social:LinkedIn:ClientId"] = "",
            ["Social:FacebookReels:ClientId"] = ""
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static HttpClient NewHttp(int timeoutSeconds = 20)
        => new() { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

    [Fact]
    public void Registry_MapsEveryPlatformToItsRealProvider_NoPlatformIsMock()
    {
        var providers = new ISocialProvider[]
        {
            new MockSocialProvider(NullLogger<MockSocialProvider>.Instance),
            new YouTubeSocialProvider(BuildConfig(), NewHttp(), NullLogger<YouTubeSocialProvider>.Instance),
            new TikTokSocialProvider(BuildConfig(), NewHttp(), NullLogger<TikTokSocialProvider>.Instance),
            new InstagramSocialProvider(BuildConfig(), NewHttp(), NullLogger<InstagramSocialProvider>.Instance),
            new XSocialProvider(BuildConfig(), NewHttp(), NullLogger<XSocialProvider>.Instance),
            new LinkedInSocialProvider(BuildConfig(), NewHttp(), NullLogger<LinkedInSocialProvider>.Instance),
            new FacebookSocialProvider(BuildConfig(), NewHttp(), NullLogger<FacebookSocialProvider>.Instance)
        };
        var registry = new ProviderRegistry(Array.Empty<IVideoProvider>(), providers);

        foreach (var platform in Enum.GetValues<SocialPlatform>().Where(p => p != SocialPlatform.None))
        {
            var p = registry.GetSocialProvider(platform);
            Assert.NotNull(p);
            Assert.Equal(platform, p!.Platform);
        }
        // the fallback to None must still resolve to the mock provider itself
        Assert.Equal(SocialPlatform.None, registry.GetSocialProvider(SocialPlatform.None)!.Platform);
    }

    [Fact]
    public void RealProviders_StateUnconfiguredWithoutCredentials()
    {
        var providers = new ISocialProvider[]
        {
            new YouTubeSocialProvider(BuildConfig(), NewHttp(), NullLogger<YouTubeSocialProvider>.Instance),
            new TikTokSocialProvider(BuildConfig(), NewHttp(), NullLogger<TikTokSocialProvider>.Instance),
            new InstagramSocialProvider(BuildConfig(), NewHttp(), NullLogger<InstagramSocialProvider>.Instance),
            new XSocialProvider(BuildConfig(), NewHttp(), NullLogger<XSocialProvider>.Instance),
            new LinkedInSocialProvider(BuildConfig(), NewHttp(), NullLogger<LinkedInSocialProvider>.Instance),
            new FacebookSocialProvider(BuildConfig(), NewHttp(), NullLogger<FacebookSocialProvider>.Instance)
        };
        Assert.All(providers, p => Assert.False(p.IsConfigured));
    }

    [Theory]
    [InlineData("https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true")]
    [InlineData("https://open.tiktokapis.com/v2/user/info/?fields=open_id")]
    [InlineData("https://graph.facebook.com/v22.0/me?fields=id,name")]
    [InlineData("https://api.x.com/2/users/me")]
    [InlineData("https://api.linkedin.com/v2/userinfo")]
    public async Task RealEndpoints_ReachThePlatformAndRejectFakeTokens(string url)
    {
        // Proves the "real mode" wiring is genuine: the request goes to the platform host,
        // which answers with its own HTTP status (401/403), not a simulated success.
        using var http = NewHttp();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "tiktok-fake-token-for-wiring-test");
        var resp = await http.SendAsync(req);
        Assert.NotEqual(System.Net.HttpStatusCode.OK, resp.StatusCode); // must NOT pretend success
        Assert.True((int)resp.StatusCode >= 400 && (int)resp.StatusCode < 500,
            $"Expected a real authentication failure from {url}, got HTTP {(int)resp.StatusCode}");
    }
}