using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Library.Sites.Twitter;

public static class TwitterImageSyndicationServiceRegistration
{
    public static IServiceCollection AddTwitterImageSyndicationClient(this IServiceCollection services)
    {
        services.AddJsonApiClient<ITwitterImageSyndicationClient, TwitterImageSyndicationClient>();
        return services;
    }
}
