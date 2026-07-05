using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Library.Sites.ArtStation;

public static class ArtStationServiceRegistration
{
    public static IServiceCollection AddArtStationClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IArtStationClient, ArtStationClient>();
        return services;
    }
}
