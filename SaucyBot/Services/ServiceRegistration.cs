using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Services;

public static class CoreServiceRegistration
{
    public static IServiceCollection AddSaucyBotServices(this IServiceCollection services)
    {
        services.AddSingleton<SiteManager>();
        services.AddSingleton<MessageManager>();
        services.AddSingleton<DatabaseManager>();
        services.AddSingleton<IGuildConfigurationManager, GuildConfigurationManager>();
        return services;
    }
}
