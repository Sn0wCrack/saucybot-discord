using System.Reflection;
using SaucyBot.Site;

namespace SaucyBot.Services;

public sealed class SiteRegistry
{
    private readonly ILogger<SiteRegistry> _logger;
    private readonly IConfiguration _configuration;

    private readonly Dictionary<string, IBaseSite> _sites = new();

    public SiteRegistry(ILogger<SiteRegistry> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;

        var disabled = _configuration.GetSection("Bot:DisabledSites").Get<string[]>() ?? [];

        var siteInterfaces = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.Namespace != null
                        && t.Namespace.StartsWith("SaucyBot.Site.")
                        && t.IsInterface
                        && typeof(IBaseSite).IsAssignableFrom(t))
            .ToList();

        foreach (var siteInterface in siteInterfaces)
        {
            _logger.LogDebug("Attempting to start site module: {Site}", siteInterface.ToString());

            if (serviceProvider.GetService(siteInterface) is not IBaseSite instance)
            {
                _logger.LogDebug("Failed to start site module: {Site}", siteInterface.ToString());
                continue;
            }

            if (disabled.Contains(instance.Identifier))
            {
                _logger.LogDebug("Did not start site module: {Site}, as it is disabled in configuration", siteInterface.ToString());
                continue;
            }

            _logger.LogDebug("Successfully started site module: {Site}", siteInterface.ToString());

            _sites.Add(instance.Identifier, instance);
        }
    }

    public IEnumerable<KeyValuePair<string, IBaseSite>> Sites => _sites;

    public IBaseSite this[string identifier] => _sites[identifier];
}
