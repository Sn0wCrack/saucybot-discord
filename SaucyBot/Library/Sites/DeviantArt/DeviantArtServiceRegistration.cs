namespace SaucyBot.Library.Sites.DeviantArt;

public static class DeviantArtServiceRegistration
{
    public static IServiceCollection AddDeviantArtOpenEmbedClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IDeviantArtOpenEmbedClient, DeviantArtOpenEmbedClient>();
        return services;
    }

    public static IServiceCollection AddDeviantArtClient(this IServiceCollection services)
    {
        services.AddSingleton<IDeviantArtClient, DeviantArtClient>();
        return services;
    }
}
