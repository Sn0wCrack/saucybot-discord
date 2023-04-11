using SaucyBot.Extensions;
using Xunit;

namespace SaucyBot.Tests.Unit.Extensions;

public class StringExtensionsTest
{
    [Fact]
    public void IsInTest()
    {
        Assert.True("Test".IsIn("Test"));
        Assert.False("Test".IsIn("Not Test"));
    }
}