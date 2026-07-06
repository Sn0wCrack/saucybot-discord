using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

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
        
        services.AddHttpClient<IHentaiFoundryClient, HentaiFoundryClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
        });

        return services;
    }
}
