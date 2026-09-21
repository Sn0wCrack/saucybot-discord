namespace SaucyBot.Library.Sites.Twitter;

public static class VxTwitterServiceRegistration
{
    public static IServiceCollection AddVxTwitterClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IVxTwitterClient, VxTwitterClient>();
        return services;
    }
}
