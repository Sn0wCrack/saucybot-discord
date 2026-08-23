using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace SaucyBot.Common;

public static partial class Helper
{
    private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    [GeneratedRegex(@"[\u2700-\u27BF]|[\uE000-\uF8FF]|\uD83C[\uDC00-\uDFFF]|\uD83D[\uDC00-\uDFFF]|[\u2011-\u26FF]|\uD83E[\uDD10-\uDDFF]")]
    private static partial Regex EmojiRegex();

    public static string? HtmlToMarkdown(string html)
    {
        return HtmlToMarkdownConverter.Convert(html);
    }

    public static string MarkdownToPlainText(string markdown)
    {
        return Markdown.ToPlainText(markdown);
    }

    public static string ProcessDescription(string description, int maxLength = 500, string suffix = "...")
    {
        description = HtmlToMarkdown(description) ?? "";

        if (description.Length > maxLength)
        {
            description = string.Concat(description.AsSpan(0, maxLength), suffix);
        }

        return description;
    }

    public static string RandomString(int length = 8)
    {
        return new string(Enumerable.Repeat(Characters, length).Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }

    public static string EscapeDiscordMarkdown(string text)
    {
        return text
            .Replace(@"\", @"\\")
            .Replace(">", @"\>")
            .Replace("*", @"\*")
            .Replace("_", @"\_")
            .Replace("~", @"\~")
            .Replace("`", @"\`")
            .Replace("|", @"\|");
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

    public static string RemoveEmojis(string input)
    {
        return string.IsNullOrEmpty(input)
            ? input
            // Remove emojis and fix any resulting double spaces
            : EmojiRegex().Replace(input, "");
    }
}
