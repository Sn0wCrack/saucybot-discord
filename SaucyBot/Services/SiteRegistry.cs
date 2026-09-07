using System.Reflection;
using System.Text.RegularExpressions;
using SaucyBot.Site;

namespace SaucyBot.Services;

public sealed class SiteRegistry
{
    private readonly ILogger<SiteRegistry> _logger;
    private readonly IConfiguration _configuration;

    private readonly Dictionary<string, SiteMetadata> _sites = new();

    public IEnumerable<KeyValuePair<string, SiteMetadata>> Sites => _sites;

    public SiteRegistry(
        ILogger<SiteRegistry> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IEnumerable<SiteRegistration> registrations
    )
    {
        _logger = logger;
        _configuration = configuration;

        var disabled = _configuration.GetSection("Bot:DisabledSites").Get<string[]>() ?? [];

        var siteRegistrations = registrations
            .Select(registration =>
                (Registration: registration, Identifier: GetIdentifier(registration.ImplementationType)))
            .ToList();

        var duplicate = siteRegistrations
            .GroupBy(registration => registration.Identifier, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate site identifier registration: {duplicate.Key}");
        }

        using var scope = serviceProvider.CreateScope();

        foreach (var (registration, identifier) in siteRegistrations)
        {
            if (disabled.Contains(identifier, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Did not start site module: {Site}, as it is disabled in configuration", identifier);
                continue;
            }

            _logger.LogDebug("Attempting to start site module: {Site}", identifier);

            if (scope.ServiceProvider.GetService(registration.ImplementationType) is not IBaseSite instance)
            {
                throw new InvalidOperationException(
                    $"Site '{identifier}' is registered with implementation "
                    + $"'{registration.ImplementationType.Name}', but that implementation is not registered.");
            }

            _logger.LogDebug("Successfully started site module: {Site}", identifier);

            _sites.Add(identifier, new SiteMetadata(registration.ImplementationType, instance.Pattern));
        }
    }

    private static string GetIdentifier(Type implementationType) =>
        implementationType.GetCustomAttribute<SiteIdentifierAttribute>()?.Identifier
        ?? throw new InvalidOperationException(
            $"Site implementation '{implementationType.Name}' has no {nameof(SiteIdentifierAttribute)}.");

    public bool HasMatch(string content) => _sites.Values.Any(site => site.Pattern.IsMatch(content));

    public IBaseSite Resolve(string identifier, IServiceProvider provider)
    {
        if (!_sites.TryGetValue(identifier, out var metadata))
        {
            throw new KeyNotFoundException($"Unknown site identifier: {identifier}");
        }

        if (provider.GetService(metadata.ImplementationType) is not IBaseSite site)
        {
            throw new InvalidOperationException(
                $"Site '{identifier}' is registered with implementation "
                + $"'{metadata.ImplementationType.Name}', but that implementation is not registered.");
        }

        if (!string.Equals(site.Identifier, identifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Site registration '{identifier}' does not match implementation identifier "
                + $"'{site.Identifier}'.");
        }

        return site;
    }

}

public sealed record SiteMetadata(Type ImplementationType, Regex Pattern);
