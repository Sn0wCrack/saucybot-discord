using System.Reflection;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using SaucyBot.Site;
using SaucyBot.Site.Response;

namespace SaucyBot.Services;

public class SiteManager
{
    private readonly ILogger<SiteManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, BaseSite> _sites = new();
    
    public SiteManager(ILogger<SiteManager> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;

        var disabled = _configuration.GetSection("Bot:DisabledSites").Get<string[]>();

        var siteClasses = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.Namespace == "SaucyBot.Site" && t.IsClass && !t.IsAbstract && !t.IsNested)
            .ToList();
        
        foreach (var siteClass in siteClasses)
        {
            _logger.LogDebug("Attempting to start site module: {Site}", siteClass.ToString());

            if (serviceProvider.GetService(siteClass) is not BaseSite instance)
            {
                continue;
            }

            if (disabled.Contains(instance.Identifier))
            {
                continue;
            }
            
            _logger.LogDebug("Successfully started site module: {Site}", siteClass.ToString());
            
            _sites.Add(instance.Identifier, instance);
        }

    }

    public Task<List<SiteManagerProcessResult>> Match(string message)
    {
        var results = new List<SiteManagerProcessResult>();

        var embedCount = 0;
        var maximumEmbeds = _configuration.GetSection("Bot:MaximumEmbeds").Get<int>();
        
        foreach (var (identifier, site) in _sites)
        {
            if (!site.IsMatch(message))
            {
                continue;
            }

            var matches = site.Match(message);

            foreach (Match match in matches)
            {
                results.Add(new SiteManagerProcessResult(identifier, match));
                
                embedCount++;

                if (embedCount >= maximumEmbeds)
                {
                    return Task.FromResult(results);
                }
            }
        }

        return Task.FromResult(results);
    }

    public async Task<ProcessResponse?> Process(string identifier, Match match, SocketUserMessage? message = null)
    {
        return await _sites[identifier].Process(match, message);
    }
}

public record SiteManagerProcessResult(string Site, Match Match);
