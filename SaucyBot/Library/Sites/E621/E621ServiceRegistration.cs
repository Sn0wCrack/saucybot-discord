namespace SaucyBot.Library.Sites.E621;

public static class E621ServiceRegistration
{
    public static IServiceCollection AddE621Client(this IServiceCollection services)
    {
        services.AddJsonApiClient<IE621Client, E621Client>();
        return services;
    }
}
