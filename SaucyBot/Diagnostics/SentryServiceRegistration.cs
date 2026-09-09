using Microsoft.Extensions.Options;
using Sentry;
using SentryOptions = SaucyBot.Options.SentryOptions;

namespace SaucyBot.Diagnostics;

public static class SentryServiceRegistration
{
    public static IServiceCollection AddSaucyBotSentry(this IServiceCollection services, IOptions<SentryOptions> options)
    {
        var sentryOptions = options.Value;

        if (string.IsNullOrWhiteSpace(sentryOptions.Dsn))
        {
            return services;
        }

        SentrySdk.Init(sentry =>
        {
            sentry.Dsn = sentryOptions.Dsn;
            sentry.SampleRate = sentryOptions.SampleRate;
        });

        return services;
    }
}
