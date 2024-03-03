namespace SaucyBot.Extensions;

public static class ListExtensions
{
    /// <summary>
    /// Port of the JavaScript array function "slice".
    /// https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Array/slice
    ///
    /// This function is considered safe, as it will always constrain the range of the "to" to be inside the List.
    /// </summary>
    /// <param name="source">The List being operated upon.</param>
    /// <param name="from">Zero-based index at which to start extraction.</param>
    /// <param name="to">The index of the first element to exclude from the returned List.</param>
    /// <returns>List</returns>
    public static List<T> SafeSlice<T>(this List<T> source, int from, int to)
    {
        var count = to > source.Count
            ? source.Count - from
            : to - from;

        return source.GetRange(from, count);
    }

    /// <summary>
    /// Safely gets a range from a List
    /// </summary>
    /// <param name="source"></param>
    /// <param name="index"></param>
    /// <param name="count"></param>
    /// <returns>List</returns>
    public static List<T> SafeGetRange<T>(this List<T> source, int index, int count)
    {
        if (count >= source.Count)
        {
            count = source.Count - index;
        }

        return source.GetRange(index, count);
    }
    
    public static bool Empty<T>(this List<T> source)
    {
        return source.Count == 0;
    }

    public static bool NotEmpty<T>(this List<T> source)
    {
        return source.Count != 0;
    }

    public static bool Empty<T>(this IEnumerable<T> source)
    {
        return !source.Any();
    }

    public static bool NotEmpty<T>(this IEnumerable<T> source)
    {
        return source.Any();
    }
}
