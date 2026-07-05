using BenchmarkDotNet.Attributes;
using SaucyBot.Common;

namespace SaucyBot.Tests.Benchmark.Benchmarks;

[MemoryDiagnoser]
public class HelperBenchmarks
{
    private static readonly string ShortHtml = "<p>Hello World</p>";
    private static readonly string MediumHtml = """
        <div class="content">
            <h1>Title</h1>
            <p>This is a paragraph with <strong>bold</strong> and <em>italic</em> text.</p>
            <p>Another paragraph here with a <a href="https://example.com">link</a> included.</p>
        </div>
        """;

    private static readonly string LongHtml = $"""
        <article>
            <h1>Very Long Article Title</h1>
            <p>{string.Join("</p><p>", Enumerable.Range(0, 50).Select(i => $"Paragraph {i} with some content to make it realistic."))}</p>
        </article>
        """;

    private static readonly string ComplexHtml = """
        <html>
        <head><title>Test</title></head>
        <body>
            <div id="main">
                <h1>Complex Document</h1>
                <p>Line 1</p>
                <p>Line 2</p>
                <ul>
                    <li>Item 1</li>
                    <li>Item 2</li>
                    <li>Item 3</li>
                </ul>
                <table>
                    <tr><td>Cell 1</td><td>Cell 2</td></tr>
                </table>
                <script>alert('test');</script>
                <style>.cls{color:red;}</style>
            </div>
        </body>
        </html>
        """;

    private static readonly string MarkdownText = """
        # Heading 1
        
        This is a paragraph with **bold** and *italic* text.
        
        ## Heading 2
        
        - List item 1
        - List item 2
        - List item 3
        
        > Blockquote text
        
        `code here` and a [link](https://example.com)
        """;

    private static readonly List<KeyValuePair<string, string>> EmptyQuery = [];
    private static readonly List<KeyValuePair<string, string>> SingleQuery = [new("key1", "value1")];
    private static readonly List<KeyValuePair<string, string>> MultiQuery =
    [
        new("key1", "value1"),
        new("key2", "value2"),
        new("key3", "value3"),
        new("key4", "value4"),
    ];

    [Benchmark]
    public string RandomString_Default() => Helper.RandomString();

    [Benchmark]
    public string RandomString_Length32() => Helper.RandomString(32);

    [Benchmark]
    public string RandomString_Length128() => Helper.RandomString(128);

    [Benchmark]
    public Task<string?> HtmlToPlainText_Short() => Helper.HtmlToPlainText(ShortHtml);

    [Benchmark]
    public Task<string?> HtmlToPlainText_Medium() => Helper.HtmlToPlainText(MediumHtml);

    [Benchmark]
    public Task<string?> HtmlToPlainText_Long() => Helper.HtmlToPlainText(LongHtml);

    [Benchmark]
    public Task<string?> HtmlToPlainText_Complex() => Helper.HtmlToPlainText(ComplexHtml);

    [Benchmark]
    public Task<string> ProcessDescription_Short() => Helper.ProcessDescription(ShortHtml);

    [Benchmark]
    public Task<string> ProcessDescription_Medium() => Helper.ProcessDescription(MediumHtml);

    [Benchmark]
    public Task<string> ProcessDescription_Long() => Helper.ProcessDescription(LongHtml, 200);

    [Benchmark]
    public Task<string> ProcessDescription_Complex() => Helper.ProcessDescription(ComplexHtml);

    [Benchmark]
    public string MarkdownToPlainText() => Helper.MarkdownToPlainText(MarkdownText);

    [Benchmark]
    public string? GetUriWithQueryString_NoQuery() => Helper.GetUriWithQueryString("https://example.com", EmptyQuery);

    [Benchmark]
    public string? GetUriWithQueryString_SingleParam() => Helper.GetUriWithQueryString("https://example.com", SingleQuery);

    [Benchmark]
    public string? GetUriWithQueryString_MultiParam() => Helper.GetUriWithQueryString("https://example.com", MultiQuery);

    [Benchmark]
    public string? GetUriWithQueryString_ExistingQuery() =>
        Helper.GetUriWithQueryString("https://example.com?existing=1", SingleQuery);

    [Benchmark]
    public string? GetUriWithQueryString_WithAnchor() =>
        Helper.GetUriWithQueryString("https://example.com#section", SingleQuery);

    [Benchmark]
    public string? GetUriWithQueryString_NullUri() => Helper.GetUriWithQueryString(null, SingleQuery);
}
