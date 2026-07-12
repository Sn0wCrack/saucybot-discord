using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace SaucyBot.Library.Sites.FurAffinity;

public static class FurAffinityBbCodeCleaner
{
    public static string Clean(string bbCodeHtml)
    {
        if (string.IsNullOrEmpty(bbCodeHtml))
            return bbCodeHtml;

        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<body>{bbCodeHtml}</body>");

        TransformChildren(document, document.Body!);

        return document.Body!.InnerHtml.Trim();
    }

    private static void TransformChildren(IDocument document, IElement parent)
    {
        var childNodes = parent.ChildNodes.ToList();

        foreach (var child in childNodes)
        {
            if (child is IElement element)
            {
                TransformElement(document, element);
            }
        }
    }

    private static void TransformElement(IDocument document, IElement element)
    {
        TransformChildren(document, element);

        var classes = element.ClassList;

        if (element.TagName.Equals("CODE", StringComparison.OrdinalIgnoreCase)
            && classes.Any(c => c.Contains("bbcode_center")))
        {
            UnwrapElement(element);
            return;
        }

        if ((element.TagName.Equals("SPAN", StringComparison.OrdinalIgnoreCase)
             || element.TagName.Equals("DIV", StringComparison.OrdinalIgnoreCase))
            && classes.Any(c => c.Contains("bbcode_quote")))
        {
            ReplaceWith(document, element, "blockquote");
            return;
        }

        var bbCodeClasses = classes.Where(c => c.Contains("bbcode_")).ToList();
        foreach (var cls in bbCodeClasses)
        {
            classes.Remove(cls);
        }

        classes.Remove("auto_link");
    }

    private static void UnwrapElement(IElement element)
    {
        var parent = element.ParentElement;
        if (parent is null) return;

        foreach (var child in element.ChildNodes.ToList())
        {
            parent.InsertBefore(child, element);
        }

        element.Remove();
    }

    private static void ReplaceWith(IDocument document, IElement element, string tagName)
    {
        var parent = element.ParentElement;
        if (parent is null) return;

        var replacement = document.CreateElement(tagName);

        foreach (var child in element.ChildNodes.ToList())
        {
            replacement.AppendChild(child);
        }

        parent.ReplaceChild(replacement, element);
    }
}
