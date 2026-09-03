using System.Net;
using System.Net.Http.Headers;

namespace SaucyBot.Library.Sites.Pixiv;

public static class PixivServiceRegistration
{
    public static IServiceCollection AddPixivClient(this IServiceCollection services, IConfiguration configuration)
    {
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie
        {
            Name = "PHPSESSID",
            Value = configuration.GetSection("Sites:Pixiv:SessionCookie").Get<string>(),
            Domain = ".pixiv.net",
            Path = "/",
            HttpOnly = true,
            Secure = true,
        });

        services.AddHttpClient<IPixivClient, PixivClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Referrer = new Uri("https://www.pixiv.net");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(Random.Shared.Next(0, 31)));

        return services;
    }
}
