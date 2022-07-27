using System.Globalization;

namespace SaucyBot.Extensions;

public static class StringExtensions
{
    public static bool IsIn(this string source, params string[] values)
    {
        return values.Contains(source);
    }
    
    public static string ToTitleCase(this string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
}
