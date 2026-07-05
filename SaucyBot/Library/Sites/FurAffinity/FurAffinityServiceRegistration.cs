using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Library.Sites.FurAffinity;

public static class FurAffinityServiceRegistration
{
    public static IServiceCollection AddFurAffinityClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IFurAffinityClient, FaExportClient>();
        return services;
    }
}
