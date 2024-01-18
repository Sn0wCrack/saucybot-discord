using System.Linq;
using SaucyBot.Extensions;
using Xunit;

namespace SaucyBot.Tests.Unit.Extensions;

public class ListExtensionsTest
{
    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(5, 10, 5)]
    public void SafeSliceTest(int from, int to, int output)
    {
        var list = Enumerable.Range(0, 10).ToList();
        
        var sliced = list.SafeSlice(from, to);

        Assert.Equal(output, sliced.Count);
    }

    [Theory]
    [InlineData(0, 100, 10)]
    [InlineData(5, 12, 5)]
    public void SafeSliceWillConstrainToValueIntoValidRange(int from, int to, int output)
    {
        var list = Enumerable.Range(0, 10).ToList();

        var sliced = list.SafeSlice(from, to);
        
        Assert.Equal(output, sliced.Count);
    }

    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(5, 5, 5)]
    public void SafeGetRangeTest(int index, int count, int output)
    {
        var list = Enumerable.Range(0, 10).ToList();

        var sliced = list.SafeGetRange(index, count);
        
        Assert.Equal(output, sliced.Count);
    }

    [Theory]
    [InlineData(0, 100, 10)]
    [InlineData(5, 10, 5)]
    [InlineData(5, 12, 5)]
    public void SafeGetRangeWillConstrainCountToValueInValidRange(int index, int count, int output)
    {
        var list = Enumerable.Range(0, 10).ToList();

        var range = list.SafeGetRange(index, count);
        
        Assert.Equal(output, range.Count);
    }
}