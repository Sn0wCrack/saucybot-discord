using System.Net;
using System.Text;

namespace SaucyBot.Common;

public static class HtmlToMarkdownConverter
{
    public static string? Convert(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var output = new StringBuilder(html.Length);
        var currentBuffer = output;
        StringBuilder? linkBuffer = null;
        string? linkUrl = null;

        for (var i = 0; i < html.Length; i++)
        {
            if (html[i] != '<')
            {
                if (html[i] is not '\n' and not '\r')
                    currentBuffer.Append(html[i]);
                continue;
            }

            if (i + 3 < html.Length && html[i + 1] == '!' && html[i + 2] == '-' && html[i + 3] == '-')
            {
                var commentEnd = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (commentEnd > i)
                {
                    i = commentEnd + 2;
                    continue;
                }
            }

            var tagEnd = html.IndexOf('>', i + 1);
            if (tagEnd == -1)
                break;

            var tagLength = tagEnd - i - 1;
            if (tagLength <= 0)
            {
                i = tagEnd;
                continue;
            }

            var tagContent = html.AsSpan(i + 1, tagLength);
            var isClosing = tagContent[0] == '/';
            var nameStart = isClosing ? 1 : 0;

            var nameEnd = nameStart;
            while (nameEnd < tagContent.Length && tagContent[nameEnd] is not (' ' or '\t' or '\n' or '\r' or '/'))
                nameEnd++;

            var tagName = tagContent[nameStart..nameEnd];

            if (!isClosing && tagName.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                var attrPart = tagContent[nameEnd..].Trim();
                linkUrl = ExtractHref(attrPart);
            }

            if (tagName.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                if (!isClosing)
                    currentBuffer.Append("\n\n");
            }
            else if (tagName.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                currentBuffer.Append('\n');
            }
            else if (tagName.Equals("hr", StringComparison.OrdinalIgnoreCase))
            {
                currentBuffer.Append("\n---\n");
            }
            else if (tagName is "strong" or "b")
            {
                currentBuffer.Append("**");
            }
            else if (tagName is "em" or "i")
            {
                currentBuffer.Append('*');
            }
            else if (tagName is "s" or "del" or "strike")
            {
                currentBuffer.Append("~~");
            }
            else if (tagName.Equals("u", StringComparison.OrdinalIgnoreCase))
            {
                currentBuffer.Append("__");
            }
            else if (tagName.Equals("code", StringComparison.OrdinalIgnoreCase))
            {
                currentBuffer.Append('`');
            }
            else if (tagName.Equals("pre", StringComparison.OrdinalIgnoreCase))
            {
                if (!isClosing)
                    currentBuffer.Append("\n```\n");
                else
                    currentBuffer.Append("\n```\n");
            }
            else if (tagName.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                if (!isClosing)
                {
                    linkBuffer = new StringBuilder();
                    currentBuffer = linkBuffer;
                }
                else if (linkBuffer is not null)
                {
                    var linkText = linkBuffer.ToString();
                    if (IsBareUrl(linkUrl, linkText))
                    {
                        output.Append(linkUrl!);
                    }
                    else if (!string.IsNullOrEmpty(linkText))
                    {
                        if (!string.IsNullOrEmpty(linkUrl))
                        {
                            output.Append('[');
                            output.Append(linkText);
                            output.Append("](");
                            output.Append(linkUrl);
                            output.Append(')');
                        }
                        else
                        {
                            output.Append(linkText);
                        }
                    }
                    else if (!string.IsNullOrEmpty(linkUrl))
                    {
                        output.Append(linkUrl);
                    }
                    linkBuffer = null;
                    linkUrl = null;
                    currentBuffer = output;
                }
            }
            else if (tagName.Length == 2 && tagName[0] == 'h' && tagName[1] >= '1' && tagName[1] <= '6')
            {
                if (!isClosing)
                {
                    if (currentBuffer.Length > 0)
                        currentBuffer.Append('\n');
                    currentBuffer.Append('#', tagName[1] - '0');
                    currentBuffer.Append(' ');
                }
            }
            else if (tagName.Equals("ul", StringComparison.OrdinalIgnoreCase) || tagName.Equals("ol", StringComparison.OrdinalIgnoreCase))
            {
                if (isClosing)
                    currentBuffer.Append('\n');
            }
            else if (tagName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                if (!isClosing)
                    currentBuffer.Append("\n- ");
            }
            else if (tagName.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
            {
                if (!isClosing)
                    currentBuffer.Append("\n> ");
                else
                    currentBuffer.Append('\n');
            }

            i = tagEnd;
        }

        return WebUtility.HtmlDecode(output.ToString());
    }

    private static bool IsBareUrl(string? url, string text)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(text))
            return false;

        if (url.Equals(text, StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedUrl = url;
        if (normalizedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            normalizedUrl = normalizedUrl["http://".Length..];
        else if (normalizedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            normalizedUrl = normalizedUrl["https://".Length..];
        normalizedUrl = normalizedUrl.TrimEnd('/');

        var normalizedText = text;
        if (normalizedText.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            normalizedText = normalizedText["http://".Length..];
        else if (normalizedText.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            normalizedText = normalizedText["https://".Length..];
        normalizedText = normalizedText.TrimEnd('/');

        return normalizedUrl.Equals(normalizedText, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractHref(ReadOnlySpan<char> attributes)
    {
        if (attributes.Length < 4)
            return null;

        var hrefPos = attributes.Length;

        for (var i = 0; i <= attributes.Length - 4; i++)
        {
            if ((i == 0 || attributes[i - 1] is ' ' or '\t') &&
                (attributes[i] == 'h' || attributes[i] == 'H') &&
                (attributes[i + 1] == 'r' || attributes[i + 1] == 'R') &&
                (attributes[i + 2] == 'e' || attributes[i + 2] == 'E') &&
                (attributes[i + 3] == 'f' || attributes[i + 3] == 'F'))
            {
                hrefPos = i;
                break;
            }
        }

        if (hrefPos >= attributes.Length)
            return null;

        var afterHref = attributes[(hrefPos + 4)..].TrimStart();

        if (afterHref.Length == 0 || afterHref[0] != '=')
            return null;

        afterHref = afterHref[1..].TrimStart();

        if (afterHref.Length == 0)
            return null;

        int valueStart;
        int valueEnd;

        if (afterHref[0] is '"' or '\'')
        {
            var quote = afterHref[0];
            valueStart = 1;
            valueEnd = afterHref[1..].IndexOf(quote);
            if (valueEnd == -1)
                valueEnd = afterHref.Length - 1;
            else
                valueEnd += 1;
        }
        else
        {
            valueStart = 0;
            valueEnd = afterHref.IndexOfAny(' ', '\t', '>');
            if (valueEnd == -1)
                valueEnd = afterHref.Length;
        }

        return afterHref.Slice(valueStart, valueEnd - valueStart).ToString();
    }
}
