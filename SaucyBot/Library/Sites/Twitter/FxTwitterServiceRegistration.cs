namespace SaucyBot.Library.Sites.Twitter;

public static class FxTwitterServiceRegistration
{
    public static IServiceCollection AddFxTwitterClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IFxTwitterClient, FxTwitterClient>();
        return services;
    }
}
