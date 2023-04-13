using AngleSharp;
using AngleSharp.Dom;

namespace SaucyBot.Common;

public sealed class PlainTextMarkupFormatter : IMarkupFormatter
{
    public String Text(ICharacterData text)
    {
        return text.Data;
    }
    
    public String LiteralText(ICharacterData text)
    {
        return text.Data;
    }

    public String Comment(IComment comment)
    {
        return String.Empty;
    }
    
    public String Processing(IProcessingInstruction processing)
    {
        return String.Empty;
    }

    public String Doctype(IDocumentType doctype)
    {
        return String.Empty;
    }

    public String OpenTag(IElement element, Boolean selfClosing)
    {
        return String.Empty;
    }

    public String CloseTag(IElement element, Boolean selfClosing)
    {
        return String.Empty;
    }
}
