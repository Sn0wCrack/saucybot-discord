using System.Net;

namespace SaucyBot.Library.Sites.HentaiFoundry;

public static class HentaiFoundryServiceRegistration
{
    public static IServiceCollection AddHentaiFoundryClient(this IServiceCollection services, IConfiguration configuration)
    {
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie
        {
            Name = "PHPSESSID",
            Value = WebUtility.UrlDecode(configuration.GetSection("Sites:HentaiFoundry:SessionCookie").Get<string>()),
            Domain = "www.hentai-foundry.com",
            Path = "/",
            HttpOnly = true,
            Secure = false,
        });

        services.AddHtmlClient<IHentaiFoundryClient, HentaiFoundryClient>(
            new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true,
            }
        );

        return services;
    }
}
