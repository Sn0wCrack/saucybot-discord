using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Library.Sites.HentaiFoundry;

public static class HentaiFoundryServiceRegistration
{
    public static IServiceCollection AddHentaiFoundryClient(this IServiceCollection services)
    {
        services.AddHttpClient<IHentaiFoundryClient, HentaiFoundryClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
        });

        return services;
    }
}
