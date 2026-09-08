using System.Net;
using SaucyBot.Options.Sites;

namespace SaucyBot.Library.Sites.HentaiFoundry;

public static class HentaiFoundryServiceRegistration
{
    public static IServiceCollection AddHentaiFoundryClient(this IServiceCollection services, IConfiguration configuration)
    {
        var hentaiFoundryOptions = configuration.GetSection("Sites:HentaiFoundry").Get<HentaiFoundryOptions>() ?? new();

        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie
        {
            Name = "PHPSESSID",
            Value = WebUtility.UrlDecode(hentaiFoundryOptions.SessionCookie),
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
