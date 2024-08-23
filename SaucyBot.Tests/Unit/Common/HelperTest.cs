using System.Threading.Tasks;
using SaucyBot.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Common;

public class HelpersTest
{
    [Theory]
    [InlineData(5)]
    [InlineData(35)]
    [InlineData(2)]
    [InlineData(168)]
    public void RandomStringWillBeGeneratedWithTheCorrectLength(int length)
    {
        var random = Helper.RandomString(length);
        
        Assert.Equal(length, random.Length);
    }
    
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(20)]
    [InlineData(8)]
    public async Task ProcessDescriptionWillLimitStringLength(int maxLength)
    {
        var random = Helper.RandomString(maxLength * 2);

        var processed = await Helper.ProcessDescription(random, maxLength, "");
        
        Assert.NotEqual(random.Length, processed.Length);
        Assert.Equal(processed.Length, maxLength);
    }

    [Theory]
    [InlineData(100, "...")]
    [InlineData(200, "")]
    [InlineData(20, "--")]
    [InlineData(8, "?!")]
    public async Task ProcessDescriptionWillAddSuffixWhenLimited(int maxLength, string suffix)
    {
        var random = Helper.RandomString(maxLength * 2);

        var processed = await Helper.ProcessDescription(random, maxLength, suffix);
        
        Assert.Equal(maxLength + suffix.Length, processed.Length);
        Assert.Equal(suffix, processed[^suffix.Length..]);
    }
    
    [Theory]
    [InlineData("<p>Test</p>")]
    [InlineData("<p><span>Test</span> Test</p")]
    [InlineData("<h1>TEST</h1>")]
    [InlineData("<script>let test = 'test';</script>")]
    public async Task ProcessDescriptionWillRemoveHtml(string description)
    {
        var processed = await Helper.ProcessDescription(description);

        Assert.NotEqual(description, processed);
    }

    [Theory]
    [InlineData("<p>Test</p>Test")]
    [InlineData("Test<br>Test")]
    public async Task ProcessDescriptionWillRetainBreaksAndParagraphs(string description)
    {
        var processed = await Helper.ProcessDescription(description);

        Assert.Contains("\n", processed);
    }
    
    [Fact]
    public async Task ProcessDescriptionWillRetainBreaksAndRemoveExistingNewLines()
    {
        const string description = "Test\n<br>Test";

        var processed = await Helper.ProcessDescription(description);

        Assert.Contains("\n", processed);
        Assert.DoesNotContain("\n\n", processed);
    }
}