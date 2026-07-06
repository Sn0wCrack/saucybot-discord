using System.Collections.Generic;
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
    public void ProcessDescriptionWillLimitStringLength(int maxLength)
    {
        var random = Helper.RandomString(maxLength * 2);

        var processed = Helper.ProcessDescription(random, maxLength, "");
        
        Assert.NotEqual(random.Length, processed.Length);
        Assert.Equal(processed.Length, maxLength);
    }

    [Theory]
    [InlineData(100, "...")]
    [InlineData(200, "")]
    [InlineData(20, "--")]
    [InlineData(8, "?!")]
    public void ProcessDescriptionWillAddSuffixWhenLimited(int maxLength, string suffix)
    {
        var random = Helper.RandomString(maxLength * 2);

        var processed = Helper.ProcessDescription(random, maxLength, suffix);
        
        Assert.Equal(maxLength + suffix.Length, processed.Length);
        Assert.Equal(suffix, processed[^suffix.Length..]);
    }
    
    [Theory]
    [InlineData("<p>Test</p>")]
    [InlineData("<p><span>Test</span> Test</p>")]
    [InlineData("<h1>TEST</h1>")]
    [InlineData("<script>let test = 'test';</script>")]
    public void ProcessDescriptionWillRemoveHtml(string description)
    {
        var processed = Helper.ProcessDescription(description);
        
        Assert.NotEqual(description, processed);
    }
    
    [Theory]
    [InlineData("<p>Test</p>Test")]
    [InlineData("Test<br>Test")]
    public void ProcessDescriptionWillRetainBreaksAndParagraphs(string description)
    {
        var processed = Helper.ProcessDescription(description);
        
        Assert.Contains("\n", processed);
    }
    
    [Fact]
    public void ProcessDescriptionWillRetainBreaksAndRemoveExistingNewLines()
    {
        const string description = "Test\n<br>Test";

        var processed = Helper.ProcessDescription(description);
        
        Assert.Contains("\n", processed);
        Assert.DoesNotContain("\n\n", processed);
    }

    [Theory]
    [InlineData("<p>Hello World</p>", "\n\nHello World")]
    [InlineData("<div><p>Paragraph 1</p><p>Paragraph 2</p></div>", "\n\nParagraph 1\n\nParagraph 2")]
    [InlineData("<h1>Title</h1><p>Content with <strong>bold</strong> text</p>", "# Title\n\nContent with **bold** text")]
    [InlineData("<br/>Line 1<br/>Line 2", "\nLine 1\nLine 2")]
    [InlineData("<p>Line 1Line 2</p>", "\n\nLine 1Line 2")]
    [InlineData("<a href=\"http://example.com\">example.com</a>", "http://example.com")]
    [InlineData("<a href=\"https://example.com/page\">https://example.com/page</a>", "https://example.com/page")]
    [InlineData("<a href=\"https://example.com/page/\">example.com/page</a>", "https://example.com/page/")]
    [InlineData("<a href=\"https://example.com\">click here</a>", "[click here](https://example.com)")]
    [InlineData("<a href=\"http://example.com\">http://example.com</a>", "http://example.com")]
    [InlineData("<a href=\"https://www.imaworldbuilder.com\">www.imaworldbuilder.com</a>", "https://www.imaworldbuilder.com")]
    public void HtmlToMarkdown_ReturnsExpectedText(string html, string expected)
    {
        var result = Helper.HtmlToMarkdown(html);
        
        Assert.Equal(expected, result);
    }



    [Theory]
    [InlineData("**bold**", "bold\n")]
    [InlineData("*italic*", "italic\n")]
    [InlineData("`code`", "code\n")]
    [InlineData("# Heading", "Heading\n")]
    [InlineData("[link](https://example.com)", "link\n")]
    [InlineData("Plain text", "Plain text\n")]
    public void MarkdownToPlainText_ReturnsExpectedText(string markdown, string expected)
    {
        var result = Helper.MarkdownToPlainText(markdown);
        
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("abc", new string[] { }, "abc")]
    [InlineData("abc", new[] { "key1=value1" }, "abc?key1=value1")]
    [InlineData("abc?existing=1", new[] { "key1=value1" }, "abc?existing=1&key1=value1")]
    [InlineData("abc#anchor", new[] { "key1=value1" }, "abc?key1=value1#anchor")]
    [InlineData("abc?existing=1#anchor", new[] { "key1=value1" }, "abc?existing=1&key1=value1#anchor")]
    [InlineData("abc", new[] { "key1=value1", "key2=value2" }, "abc?key1=value1&key2=value2")]
    [InlineData(null, new[] { "key1=value1" }, null)]
    public void GetUriWithQueryString_ReturnsExpectedUri(string? uri, string[] queryStringArray, string? expected)
    {
        var queryString = new List<KeyValuePair<string, string>>();
        foreach (var item in queryStringArray)
        {
            var parts = item.Split('=');
            queryString.Add(new KeyValuePair<string, string>(parts[0], parts[1]));
        }
        
        var result = Helper.GetUriWithQueryString(uri, queryString);
        
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetUriWithQueryString_EncodesSpecialCharacters()
    {
        var queryString = new List<KeyValuePair<string, string>>
        {
            new("key", "value with spaces"),
            new("special&key", "value&special")
        };
        
        var result = Helper.GetUriWithQueryString("https://example.com", queryString);
        
        Assert.Contains("key=value+with+spaces", result);
        Assert.Contains("special%26key=value%26special", result);
    }
}
