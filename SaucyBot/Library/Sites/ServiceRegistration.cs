using System.Net.Http.Headers;

namespace SaucyBot.Library.Sites;

internal static class HttpClientRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        public IHttpClientBuilder AddHtmlClient<TInterface, TImplementation>(HttpClientHandler? handler = null)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            return services.AddHttpClient<TInterface, TImplementation>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/avif"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/apng"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

                client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
                client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.9));

                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(Random.Shared.Next(0, 31)))
            .ConfigurePrimaryHttpMessageHandler(() => handler ?? new HttpClientHandler { AllowAutoRedirect = true });

        }

        public IHttpClientBuilder AddJsonApiClient<TInterface, TImplementation>(string userAgent = "SaucyBot/0.0.0 (https://github.com/Sn0wCrack/saucybot-discord)")
            where TInterface : class
            where TImplementation : class, TInterface
        {
            return services.AddHttpClient<TInterface, TImplementation>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(Random.Shared.Next(0, 31)));
        }

        public IServiceCollection AddFileDownloadClient()
        {
            services.AddHttpClient("FileDownload", client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(Random.Shared.Next(0, 31)));

            return services;
        }
    }
}
