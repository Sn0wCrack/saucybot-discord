using System.Reflection;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site;
using SaucyBot.Site.Response;

namespace SaucyBot.Services;

public class SiteManager
{
    private readonly ILogger<SiteManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly MessageManager _messageManager;
    private readonly GuildConfigurationManager _guildConfigurationManager;
    
    private readonly Dictionary<string, BaseSite> _sites = new();
    
    public SiteManager(
        ILogger<SiteManager> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        MessageManager messageManager,
        GuildConfigurationManager guildConfigurationManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _messageManager = messageManager;
        _guildConfigurationManager = guildConfigurationManager;

        var disabled = _configuration.GetSection("Bot:DisabledSites").Get<string[]>();

        var siteClasses = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.Namespace == "SaucyBot.Site" && t.IsClass && t.BaseType?.FullName == "SaucyBot.Site.BaseSite")
            .ToList();
        
        foreach (var siteClass in siteClasses)
        {
            _logger.LogDebug("Attempting to start site module: {Site}", siteClass.ToString());

            if (serviceProvider.GetService(siteClass) is not BaseSite instance)
            {
                _logger.LogDebug("Failed to start site module: {Site}", siteClass.ToString());
                continue;
            }

            if (disabled.Contains(instance.Identifier))
            {
                _logger.LogDebug("Did not start site module: {Site}, as it is disabled in configuration", siteClass.ToString());
                continue;
            }
            
            _logger.LogDebug("Successfully started site module: {Site}", siteClass.ToString());
            
            _sites.Add(instance.Identifier, instance);
        }

    }

    public async Task<List<SiteManagerProcessResult>> Match(SocketUserMessage message)
    {
        var results = new List<SiteManagerProcessResult>();
        
        var embedCount = 0u;
        
        var guildConfiguration = await _guildConfigurationManager.GetByChannel(message.Channel);
        
        var maximumEmbeds = guildConfiguration?.MaximumEmbeds ?? _configuration.GetSection("Bot:MaximumEmbeds").Get<uint>();

        foreach (var (identifier, site) in _sites)
        {
            var content = message.CleanContent;
            
            if (!site.IsMatch(content))
            {
                continue;
            }

            var matches = site.Match(content);

            foreach (Match match in matches)
            {
                results.Add(new SiteManagerProcessResult(identifier, match));

                embedCount++;

                if (embedCount >= maximumEmbeds)
                {
                    return results;
                }
            }
        }

        return results;
    }

    public async Task Handle(SocketUserMessage message)
    {
        var results = await Match(message);
        
        if (!results.Any())
        {
            return;
        }
        
        foreach (var (site, match) in results)
        {
            _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);
            
            var matchedMessage = await SendMatchedMessage(message, site);

            try
            {
                var response = await Process(site, match, message);

                if (response is null)
                {
                    _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);

                    if (matchedMessage is not null)
                    {
                        await matchedMessage.DeleteAsync();
                    }

                    continue;
                }

                await _messageManager.Send(message, response);

                if (matchedMessage is not null)
                {
                    await matchedMessage.DeleteAsync();
                }
            }
            catch (Exception ex)
            {
                if (matchedMessage is not null)
                {
                    await matchedMessage.DeleteAsync();
                }
                
                _logger.LogError(ex, "Exception occured processing or sending messages");
            }
        }
    }

    private async Task<IUserMessage?> SendMatchedMessage(SocketUserMessage message, string site)
    {
        var guildConfiguration = await _guildConfigurationManager.GetByChannel(message.Channel);

        if (guildConfiguration is null || !guildConfiguration.SendMatchedMessage)
        {
            return null;
        }

        return await message.ReplyAsync($"Matched link to {site}, please wait...", allowedMentions: AllowedMentions.None);
    }

    public async Task<ProcessResponse?> Process(string identifier, Match match, SocketUserMessage? message = null)
    {
        return await _sites[identifier].Process(match, message);
    }
}

public record SiteManagerProcessResult(string Site, Match Match);
