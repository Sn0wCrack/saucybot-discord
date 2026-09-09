using Microsoft.Extensions.Configuration;

namespace SaucyBot.Extensions;

public static class ConfigurationOptionsExtensions
{
    public static TOptions BindOrDefault<TOptions>(this IConfiguration configuration, string section)
        where TOptions : class, new() =>
        configuration.GetSection(section).Get<TOptions>() ?? new TOptions();
}
