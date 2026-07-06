using System.Net;
using System.Text;
using Markdig;

namespace SaucyBot.Common;

public static class Helper
{
    private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string? HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var length = html.Length;
        var sb = new StringBuilder(length);

        var inTag = false;
        var tagNameStart = 0;

        for (var i = 0; i < length; i++)
        {
            var c = html[i];

            if (c == '<')
            {
                if (i + 3 < length && html[i + 1] == '!' && html[i + 2] == '-' && html[i + 3] == '-')
                {
                    var commentEnd = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    if (commentEnd > i)
                    {
                        i = commentEnd + 2;
                        continue;
                    }
                }

                inTag = true;
                tagNameStart = i + 1;
                continue;
            }

            if (c == '>' && inTag)
            {
                inTag = false;

                var isClosingTag = html[tagNameStart] == '/';
                var nameStart = isClosingTag ? tagNameStart + 1 : tagNameStart;
                var tagEnd = nameStart;

                while (tagEnd < i && html[tagEnd] is not (' ' or '\t' or '\n' or '\r' or '/'))
                    tagEnd++;

                var tagName = html.AsSpan(nameStart, tagEnd - nameStart);

                if (!isClosingTag)
                {
                    if (tagName.Equals("p", StringComparison.OrdinalIgnoreCase))
                        sb.Append("\n\n");
                    else if (tagName.Equals("br", StringComparison.OrdinalIgnoreCase))
                        sb.Append('\n');
                    else if (tagName.Equals("span", StringComparison.OrdinalIgnoreCase))
                        sb.Append(' ');
                }

                continue;
            }

            if (!inTag && c is not '\n' and not '\r')
            {
                sb.Append(c);
            }
        }

        return WebUtility.HtmlDecode(sb.ToString());
    }

    public static string MarkdownToPlainText(string markdown)
    {
        return Markdown.ToPlainText(markdown);
    }

    public static string ProcessDescription(string description, int maxLength = 300, string suffix = "...")
    {
        description = HtmlToPlainText(description) ?? "";

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
