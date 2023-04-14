using System.Net;
using System.Text;
using AngleSharp;
using AngleSharp.Html.Parser;

namespace SaucyBot.Common;

public static class Helper
{
    private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    
    public static async Task<string?> HtmlToPlainText(string html)
    {
        // Since HTML does not respect newlines, we need to remove them before processing to ensure
        // our plaintext has the exact number of newlines that would be displayed on the site or thereabouts at least.
        html = html
            .Replace("\n", "")
            .Replace("\r", "");
        
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html);

        return document.Body?.ToHtml(new PlainTextMarkupFormatter());
    }

    public static async Task<string> ProcessDescription(string description, int maxLength = 300, string suffix = "...")
    {
        description = await HtmlToPlainText(description) ?? "";
        
        if (description.Length > maxLength)
        {
            description = $"{description.AsSpan(0, maxLength)}{suffix}";
        }

        return description;
    }

    public static string RandomString(int length = 8)
    {
        return new string(Enumerable.Repeat(Characters, length).Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// This is taken from Microsoft.AspNetCore.WebUtilities.QueryHelpers
    /// </summary>
    /// <param name="uri"></param>
    /// <param name="queryString"></param>
    /// <returns></returns>
    public static string? GetUriWithQueryString(string? uri, IEnumerable<KeyValuePair<string, string>> queryString)
    {
        if (uri is null)
        {
            return null;
        }
        
        var anchorIndex = uri.IndexOf('#');
        var uriToBeAppended = uri;
        var anchorText = "";
        // If there is an anchor, then the query string must be inserted before its first occurence.
        if (anchorIndex != -1)
        {
            anchorText = uri[anchorIndex..];
            uriToBeAppended = uri[..anchorIndex];
        }
        
        var hasQuery = uriToBeAppended.Contains('?');

        var sb = new StringBuilder();
        sb.Append(uriToBeAppended);
        foreach (var parameter in queryString)
        {
            sb.Append(hasQuery ? '&' : '?');
            sb.Append(WebUtility.UrlEncode(parameter.Key));
            sb.Append('=');
            sb.Append(WebUtility.UrlEncode(parameter.Value));
            hasQuery = true;
        }

        sb.Append(anchorText);
        return sb.ToString();
    }
}
