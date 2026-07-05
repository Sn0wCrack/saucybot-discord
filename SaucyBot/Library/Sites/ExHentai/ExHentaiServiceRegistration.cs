using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Library.Sites.ExHentai;

public static class ExHentaiServiceRegistration
{
    public static IServiceCollection AddExHentaiClient(this IServiceCollection services, IConfiguration configuration)
    {
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie("ipb_member_id", configuration.GetSection("Sites:ExHentai:Cookies:MemberId").Get<string>(), "/", "exhentai.org"));
        cookieContainer.Add(new Cookie("ipb_pass_hash", configuration.GetSection("Sites:ExHentai:Cookies:PasswordHash").Get<string>(), "/", "exhentai.org"));

        services.AddHttpClient<IExHentaiClient, ExHentaiClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36");
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
