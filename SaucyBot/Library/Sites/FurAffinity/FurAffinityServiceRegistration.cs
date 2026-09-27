namespace SaucyBot.Library.Sites.FurAffinity;

public static class FurAffinityServiceRegistration
{
    public static IServiceCollection AddFurAffinityClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<IFaExportClient, FaExportClient>();

        return services;
    }
}
