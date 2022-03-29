using AngleSharp.Html.Parser;
using System;
using AngleSharp.Dom;

namespace SaucyBot.Library;

public static class Helper
{
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
            description = string.Concat(description.AsSpan(0, maxLength), "...");
        }

        return description;
    }
}