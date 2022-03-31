using AngleSharp.Html.Parser;

namespace SaucyBot.Common;

public static class Helper
{
    private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    
    public static async Task<string?> HtmlToPlainText(string html)
    {
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html);

        return document.Body?.TextContent;
    }

    public static async Task<string> ProcessDescription(string description, int maxLength = 300)
    {
        description = await HtmlToPlainText(description) ?? "";
        
        if (description.Length > maxLength)
        {
            description = $"{description.AsSpan(0, maxLength)}...";
        }

        return description;
    }

    public static string RandomString(int length = 8)
    {
        var random = new Random();

        return new string(Enumerable.Repeat(Characters, length).Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
