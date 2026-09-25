namespace AIPhilosophy.Core.Domain;

public enum SocialPlatform
{
    None = 0,
    YouTubeShorts = 1,
    TikTok = 2,
    InstagramReels = 3,
    XTwitter = 4,
    LinkedIn = 5,
    FacebookReels = 6
}

public enum SubscriptionTier
{
    Free = 0,
    Pro = 1,
    Premium = 2
}

public enum ScriptStatus
{
    Draft = 0,
    Ready = 1,
    Generating = 2,
    Generated = 3,
    Failed = 4,
    Scheduled = 5,
    Published = 6
}

public enum VideoStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3,
    Posting = 4,
    Posted = 5
}

public enum GenerationProvider
{
    None = 0,
    Mock = 1,
    Replicate = 2,
    Runway = 3,
    Pika = 4,
    Kling = 5,
    HeyGen = 6
}

public enum SocialAccountStatus
{
    Active = 0,
    Revoked = 1,
    Error = 2
}

public enum PostStatus
{
    Pending = 0,
    Scheduled = 1,
    Posting = 2,
    Posted = 3,
    Failed = 4,
    Skipped = 5
}

public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Retrying = 4
}