using BenchmarkDotNet.Attributes;
using SaucyBot.Extensions;

namespace SaucyBot.Tests.Benchmark.Benchmarks;

[MemoryDiagnoser]
[MinInvokeCount(3), InvocationCount(16)]      
[MinWarmupCount(3), MaxWarmupCount(5)]
[MinIterationCount(3), MaxIterationCount(5)]
public class ExtensionsBenchmarks
{
    private List<string> _smallList = null!;
    private List<string> _largeList = null!;
    private List<int> _intList = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallList = [.. Enumerable.Range(0, 10).Select(i => $"item{i}")];
        _largeList = [.. Enumerable.Range(0, 1000).Select(i => $"item{i}")];
        _intList = [.. Enumerable.Range(0, 100)];
    }

    [Benchmark]
    public bool IsIn_SmallArray() => "item5".IsIn("item0", "item1", "item2", "item3", "item4", "item5");

    [Benchmark]
    public bool IsIn_LargeArray()
    {
        var items = new string[100];
        for (var i = 0; i < 100; i++) items[i] = $"item{i}";
        return "item99".IsIn(items);
    }

    [Benchmark]
    public bool IsIn_MissingItem() => "notfound".IsIn("item0", "item1", "item2");

    [Benchmark]
    public string ToTitleCase_Short() => "hello".ToTitleCase();

    [Benchmark]
    public string ToTitleCase_MultiWord() => "hello world from saucybot".ToTitleCase();

    [Benchmark]
    public string ToTitleCase_AlreadyTitle() => "Hello World From SaucyBot".ToTitleCase();

    [Benchmark]
    public List<string> SafeSlice_SmallList_FullRange() => _smallList.SafeSlice(0, 10);

    [Benchmark]
    public List<string> SafeSlice_LargeList_Partial() => _largeList.SafeSlice(100, 300);

    [Benchmark]
    public List<string> SafeSlice_LargeList_BeyondBounds() => _largeList.SafeSlice(900, 2000);

    [Benchmark]
    public List<int> SafeGetRange_Exact() => _intList.SafeGetRange(0, 100);

    [Benchmark]
    public List<int> SafeGetRange_BeyondBounds() => _intList.SafeGetRange(50, 200);

    [Benchmark]
    public bool Empty_NonEmptyList() => _intList.Empty();

    [Benchmark]
    public bool NotEmpty_NonEmptyList() => _intList.NotEmpty();

    [Benchmark]
    public bool Empty_EmptyList() => new List<int>().Empty();

    [Benchmark]
    public bool NotEmpty_EmptyList() => new List<int>().NotEmpty();

    [Benchmark]
    public bool EnumerableEmpty_NonEmpty() => Enumerable.Range(0, 100).Empty();

    [Benchmark]
    public bool EnumerableNotEmpty_NonEmpty() => Enumerable.Range(0, 100).NotEmpty();

    [Benchmark]
    public bool EnumerableEmpty_Empty() => Enumerable.Empty<int>().Empty();
}
