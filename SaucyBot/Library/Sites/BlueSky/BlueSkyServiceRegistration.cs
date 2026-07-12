namespace SaucyBot.Library.Sites.BlueSky;

public static class BlueSkyServiceRegistration
{
    public static IServiceCollection AddVixBlueskyClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IVixBlueskyClient, VixBlueskyClient>();
        return services;
    }
}
