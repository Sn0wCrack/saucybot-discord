using SaucyBot.Queue;
using SaucyBot.Site;

namespace SaucyBot.Services;

public static class CoreServiceRegistration
{
    public static IServiceCollection AddSaucyBotServices(this IServiceCollection services, bool databaseDisabled = false)
    {
        services.AddSingleton<SiteRegistry>();
        services.AddSingleton<InteractionHandler>();
        services.AddScoped<SiteManager>();
        services.AddScoped<IMessageWorkHandler>(services => services.GetRequiredService<SiteManager>());
        services.AddScoped<MessageManager>();
        services.AddSingleton<DiscordMessageResolver>();
        services.AddSingleton<IMessageResolver>(services => services.GetRequiredService<DiscordMessageResolver>());

        if (databaseDisabled)
        {
            services.AddSingleton<IDatabaseMigrator, NullDatabaseMigrator>();
            services.AddScoped<IGuildConfigurationManager, NullGuildConfigurationManager>();
        }
        else
        {
            services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
            services.AddScoped<IGuildConfigurationManager, GuildConfigurationManager>();
        }

        return services;
    }
}
