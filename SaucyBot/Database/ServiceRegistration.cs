namespace SaucyBot.Database;

public static class DatabaseServiceRegistration
{
    public static IServiceCollection AddSaucyBotDatabase(this IServiceCollection services)
    {
        services.AddDbContext<DatabaseContext>(ServiceLifetime.Transient);
        services.AddDbContextFactory<DatabaseContext>(lifetime: ServiceLifetime.Transient);
        return services;
    }
}
