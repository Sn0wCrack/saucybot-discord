using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Services;
using SaucyBot.Services.Cache;

namespace SaucyBot.Tests.Benchmark.Benchmarks;

[MemoryDiagnoser]
[MinInvokeCount(3), InvocationCount(16)]
[MinWarmupCount(3), MaxWarmupCount(5)]
[MinIterationCount(3), MaxIterationCount(5)]
public class MemoryCacheBenchmarks
{
    private MemoryCacheDriver _driver = null!;
    private CacheManager _cacheManager = null!;
    private IMemoryCache _memoryCache = null!;

    private const string StringKey = "test_key";
    private const string StringValue = "test_value";

    [GlobalSetup]
    public void Setup()
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Memory:DefaultLifetime"] = "3600",
            ["Cache:Driver"] = "memory",
        });
        var config = configBuilder.Build();

        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _driver = new MemoryCacheDriver(_memoryCache, config);

        var logger = Substitute.For<ILogger<CacheManager>>();
        _cacheManager = new CacheManager(logger, config, CreateServiceProvider(_driver));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _memoryCache.Dispose();
    }

    [Benchmark]
    public async Task<string?> MemoryCache_Get_Hit()
    {
        await _driver.Set(StringKey, StringValue);
        return await _driver.Get<string>(StringKey);
    }

    [Benchmark]
    public async Task<string?> MemoryCache_Get_Miss()
    {
        return await _driver.Get<string>("nonexistent_key");
    }

    [Benchmark]
    public async Task<string> MemoryCache_Set()
    {
        return await _driver.Set(StringKey, StringValue);
    }

    [Benchmark]
    public async Task<string> MemoryCache_Set_WithExpiry()
    {
        return await _driver.Set(StringKey, StringValue, TimeSpan.FromMinutes(5));
    }

    [Benchmark]
    public async Task<bool> MemoryCache_Delete()
    {
        await _driver.Set(StringKey, StringValue);
        return await _driver.Delete(StringKey);
    }

    [Benchmark]
    public async Task<string?> CacheManager_Remember_CacheMiss()
    {
        return await _cacheManager.Remember("remember_miss", () => Task.FromResult<string?>(StringValue));
    }

    [Benchmark]
    public async Task<string?> CacheManager_Remember_CacheHit()
    {
        await _cacheManager.Set("remember_hit", StringValue);
        return await _cacheManager.Remember("remember_hit", () => Task.FromResult<string?>(StringValue));
    }

    [Benchmark]
    public async Task<string?> CacheManager_Remember_WithExpiry_CacheMiss()
    {
        return await _cacheManager.Remember(
            "remember_expiry_miss",
            TimeSpan.FromMinutes(5),
            () => Task.FromResult<string?>(StringValue)
        );
    }

    [Benchmark]
    public async Task<int> CacheManager_Set_Int()
    {
        return await _cacheManager.Set("int_key", 42);
    }

    [Benchmark]
    public async Task<ComplexObject> CacheManager_Set_ComplexObject()
    {
        var obj = new ComplexObject
        {
            Id = 123,
            Name = "Test Object",
            Tags = ["tag1", "tag2", "tag3"],
            Metadata = new Dictionary<string, string>
            {
                ["key1"] = "value1",
                ["key2"] = "value2",
            }
        };
        return await _cacheManager.Set("complex_key", obj);
    }

    private static IServiceProvider CreateServiceProvider(MemoryCacheDriver driver)
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(MemoryCacheDriver)).Returns(driver);
        return provider;
    }

    public sealed record ComplexObject
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public List<string> Tags { get; init; } = [];
        public Dictionary<string, string> Metadata { get; init; } = [];
    }
}
