using SaucyBot.Services.Cache;

namespace SaucyBot.Options;

public sealed class CacheOptions
{
    public CacheDriverType? Driver { get; init; }

    public MemoryCacheOptions Memory { get; init; } = new();

    public RedisCacheOptions Redis { get; init; } = new();

    public HybridCacheOptions Hybrid { get; init; } = new();
}

public sealed class MemoryCacheOptions
{
    public long? SizeLimit { get; init; }
    public int DefaultLifetime { get; init; }
}

public sealed class RedisCacheOptions
{
    public string? ConnectionString { get; init; }
    public int DefaultLifetime { get; init; }
}

public sealed class HybridCacheOptions
{
    public int DefaultLifetime { get; init; }
}
