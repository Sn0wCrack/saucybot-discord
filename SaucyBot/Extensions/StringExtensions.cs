namespace SaucyBot.Extensions;

public static class StringExtensions
{
    public static bool IsIn(this string source, params string[] values)
    {
        return values.Contains(source);
    }
}
