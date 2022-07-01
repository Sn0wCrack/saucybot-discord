namespace SaucyBot.Extensions;

public static class ListExtensions
{
    public static List<T> Slice<T>(this List<T> source, int from, int to) => source.GetRange(from, to - from);

    public static List<T> SafeSlice<T>(this List<T> source, int from, int to)
    {
        var count = to > source.Count
            ? source.Count - from
            : to - from;

        return source.GetRange(from, count);
    }

    public static List<T> SafeGetRange<T>(this List<T> source, int index, int count)
    {
        if (count >= source.Count)
        {
            count = source.Count - index;
        }

        return source.GetRange(index, count);
    }
}
