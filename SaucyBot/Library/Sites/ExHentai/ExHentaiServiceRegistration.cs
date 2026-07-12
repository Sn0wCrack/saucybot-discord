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

        services.AddHtmlClient<IExHentaiClient, ExHentaiClient>(
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
