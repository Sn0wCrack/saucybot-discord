namespace SaucyBot.Extensions;

public static class ListExtensions
{
    public static List<T> Slice<T>(this List<T> source, int from, int to) => source.GetRange(@from, to - @from);

    public static List<T> SafeSlice<T>(this List<T> source, int from, int to)
    {
        var count = Math.Min(
            to - from,
            (source.Count - 1) - from
        );

        return source.GetRange(from, count);
    }
}
