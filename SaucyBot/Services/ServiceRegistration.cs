using SaucyBot.Queue;
using SaucyBot.Site;

namespace SaucyBot.Services;

public static class CoreServiceRegistration
{
    public static IServiceCollection AddSaucyBotServices(this IServiceCollection services)
    {
        services.AddSingleton<SiteRegistry>();
        services.AddSingleton<InteractionHandler>();
        services.AddScoped<SiteManager>();
        services.AddScoped<IMessageWorkHandler>(services => services.GetRequiredService<SiteManager>());
        services.AddScoped<MessageManager>();
        services.AddSingleton<DiscordMessageResolver>();
        services.AddSingleton<IMessageResolver>(services => services.GetRequiredService<DiscordMessageResolver>());

        services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<IGuildConfigurationManager, GuildConfigurationManager>();

        return services;
    }
}
