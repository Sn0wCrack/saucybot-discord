using SaucyBot.Common;

namespace SaucyBot.Extensions;

public static class HttpClientExtensions
{
    public static async Task<string> GetStringWithQueryStringAsync(this HttpClient client, string? requestUri, Dictionary<string, string> queryString)
    {
        var uri = Helper.GetUriWithQueryString(requestUri, queryString);

        return await client.GetStringAsync(uri);
    }
}
