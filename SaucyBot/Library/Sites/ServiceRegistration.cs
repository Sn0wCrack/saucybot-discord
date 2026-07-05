using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Library.Sites;

internal static class HttpClientRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        public IHttpClientBuilder AddJsonApiClient<TInterface, TImplementation>(string userAgent = "SaucyBot/0.0.0")
            where TInterface : class
            where TImplementation : class, TInterface
        {
            return services.AddHttpClient<TInterface, TImplementation>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            });
        }

        public IServiceCollection AddFileDownloadClient()
        {
            services.AddHttpClient("FileDownload", client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36");
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
            });
            return services;
        }
    }
}
