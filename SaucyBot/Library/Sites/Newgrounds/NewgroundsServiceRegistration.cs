using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Library.Sites.Newgrounds;

public static class NewgroundsServiceRegistration
{
    public static IServiceCollection AddNewgroundsClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<INewgroundsClient, NewgroundsClient>();
        return services;
    }
}
