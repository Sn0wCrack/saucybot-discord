namespace SaucyBot.Extensions;

public static class ListExtensions
{
    /// <param name="source">The List being operated upon.</param>
    extension<T>(List<T> source)
    {
        /// <summary>
        /// Port of the JavaScript array function "slice".
        /// https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Array/slice
        /// 
        /// This function is considered safe, as it will always constrain the range of the "to" to be inside the List.
        /// </summary>
        /// <param name="from">Zero-based index at which to start extraction.</param>
        /// <param name="to">The index of the first element to exclude from the returned List.</param>
        /// <returns>List</returns>
        public List<T> SafeSlice(int from, int to)
        {
            var count = to > source.Count
                ? source.Count - from
                : to - from;

            return source.GetRange(from, count);
        }

        /// <summary>
        /// Safely gets a range from a List
        /// </summary>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <returns>List</returns>
        public List<T> SafeGetRange(int index, int count)
        {
            if (count >= source.Count)
            {
                count = source.Count - index;
            }

            return source.GetRange(index, count);
        }

        public bool Empty()
        {
            return source.Count == 0;
        }

        public bool NotEmpty()
        {
            return source.Count != 0;
        }
    }

    extension<T>(IEnumerable<T> source)
    {
        public bool Empty()
        {
            return !source.Any();
        }

        public bool NotEmpty()
        {
            return source.Any();
        }
    }
}
