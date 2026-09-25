using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;

namespace AIPhilosophy.Infrastructure.Providers;

public interface IVideoProvider
{
    GenerationProvider Provider { get; }
    bool IsConfigured { get; }
    Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken ct);
    Task<GenerationStatusResult> CheckStatusAsync(string providerJobId, CancellationToken ct);
}

public interface ISocialProvider
{
    SocialPlatform Platform { get; }
    bool IsConfigured { get; }
    Task ValidateAccountAsync(SocialAccount account, CancellationToken ct);
    Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct);
}

public class ProviderRegistry
{
    private readonly IEnumerable<IVideoProvider> _videoProviders;
    private readonly IEnumerable<ISocialProvider> _socialProviders;

    public ProviderRegistry(IEnumerable<IVideoProvider> videoProviders, IEnumerable<ISocialProvider> socialProviders)
    {
        _videoProviders = videoProviders;
        _socialProviders = socialProviders;
    }

    public IVideoProvider? GetVideoProvider(GenerationProvider provider)
        => _videoProviders.FirstOrDefault(p => p.Provider == provider);

    public IVideoProvider? GetPreferredVideoProvider()
        => _videoProviders.FirstOrDefault(p => p.IsConfigured) ?? _videoProviders.FirstOrDefault(p => p.Provider == GenerationProvider.Mock);

    public ISocialProvider? GetSocialProvider(SocialPlatform platform)
        => _socialProviders.FirstOrDefault(p => p.Platform == platform);
}