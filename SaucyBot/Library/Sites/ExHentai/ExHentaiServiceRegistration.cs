using System.Net;
using SaucyBot.Options.Sites;

namespace SaucyBot.Library.Sites.ExHentai;

public static class ExHentaiServiceRegistration
{
    public static IServiceCollection AddExHentaiClient(this IServiceCollection services, IConfiguration configuration)
    {
        var exHentaiOptions = configuration.GetSection("Sites:ExHentai").Get<ExHentaiOptions>() ?? new();

        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie("ipb_member_id", exHentaiOptions.Cookies.MemberId, "/", "exhentai.org"));
        cookieContainer.Add(new Cookie("ipb_pass_hash", exHentaiOptions.Cookies.PasswordHash, "/", "exhentai.org"));

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
