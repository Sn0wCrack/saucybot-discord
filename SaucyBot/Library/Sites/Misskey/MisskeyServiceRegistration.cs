namespace SaucyBot.Library.Sites.Misskey;

public static class MisskeyServiceRegistration
{
    public static IServiceCollection AddMisskeyClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IMisskeyClient, MisskeyClient>();
        return services;
    }
}
