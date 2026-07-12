using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Services;

public static class CoreServiceRegistration
{
    public static IServiceCollection AddSaucyBotServices(this IServiceCollection services, bool databaseDisabled = false)
    {
        services.AddSingleton<SiteManager>();
        services.AddSingleton<MessageManager>();

        if (databaseDisabled)
        {
            services.AddSingleton<IDatabaseManager, NullDatabaseManager>();
            services.AddSingleton<IGuildConfigurationManager, NullGuildConfigurationManager>();
        }
        else
        {
            services.AddSingleton<IDatabaseManager, DatabaseManager>();
            services.AddSingleton<IGuildConfigurationManager, GuildConfigurationManager>();
        }

        return services;
    }
}
