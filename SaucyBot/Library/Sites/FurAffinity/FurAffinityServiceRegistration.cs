using System.Net;
using System.Net.Http.Headers;

namespace SaucyBot.Library.Sites.FurAffinity;

public static class FurAffinityServiceRegistration
{
    public static IServiceCollection AddFurAffinityClient(this IServiceCollection services, IConfiguration configuration)
    {
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie
        {
            Name = "a",
            Value = configuration.GetSection("Sites:FurAffinity:Cookies:A").Get<string>(),
            Domain = ".furaffinity.net",
            Path = "/",
            HttpOnly = true,
            Secure = true,
        });
        cookieContainer.Add(new Cookie
        {
            Name = "b",
            Value = configuration.GetSection("Sites:FurAffinity:Cookies:B").Get<string>(),
            Domain = ".furaffinity.net",
            Path = "/",
            HttpOnly = true,
            Secure = true,
        });

        services.AddHtmlClient<IFurAffinityClient, FurAffinityDirect>(
            new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true,
            }
        );

        // services.AddJsonApiClient<IFurAffinityClient, FaExportClient>();

        return services;
    }
}
