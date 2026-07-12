using System.Globalization;

namespace SaucyBot.Extensions;

public static class StringExtensions
{
    extension(string source)
    {
        public bool IsIn(params string[] values)
        {
            return values.Contains(source);
        }

        public string ToTitleCase() =>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(source.ToLowerInvariant());
    }
}
