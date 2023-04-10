using System.Reflection;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Site;

namespace SaucyBot.Services;

public sealed partial class SiteManager
{
    private readonly ILogger<SiteManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly MessageManager _messageManager;
    private readonly IGuildConfigurationManager _guildConfigurationManager;
    
    private readonly Dictionary<string, BaseSite> _sites = new();
    
    [GeneratedRegex(@"(<|\|\|)(?!@|#|:|a:).*(>|\|\|)", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoreContentRegex();
    
    public SiteManager(
        ILogger<SiteManager> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        MessageManager messageManager,
        IGuildConfigurationManager guildConfigurationManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _messageManager = messageManager;
        _guildConfigurationManager = guildConfigurationManager;

        var disabled = _configuration.GetSection("Bot:DisabledSites").Get<string[]>() ?? Array.Empty<string>();

        var siteClasses = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { Namespace: "SaucyBot.Site", IsClass: true, BaseType.FullName: "SaucyBot.Site.BaseSite" })
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

        var content = message.CleanContent;
        
        foreach (var (identifier, site) in _sites)
        {
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

    public async Task<List<SiteManagerProcessResult>> Match(SocketSlashCommand command)
    {
        var results = new List<SiteManagerProcessResult>();
        
        var embedCount = 0u;
        
        var guildConfiguration = await _guildConfigurationManager.GetByChannel(command.Channel);
        
        var maximumEmbeds = guildConfiguration?.MaximumEmbeds ?? _configuration.GetSection("Bot:MaximumEmbeds").Get<uint>();

        var content = (string?) command.Data.Options.First().Value;

        if (content is null)
        {
            return results;
        }
        
        foreach (var (identifier, site) in _sites)
        {
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

    public async Task HandleMessage(SocketUserMessage message)
    {
        if (!ShouldProcessMessage(message))
        {
            _logger.LogDebug("Message was ignored with content: \"{Message}\"", message.Content);
            return;
        }
        
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
                var response = await _sites[site].Process(match, message);

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
                
                await message.ModifyAsync(x => x.Flags = MessageFlags.SuppressEmbeds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occured processing or sending messages");
            }
            finally
            {
                if (matchedMessage is not null)
                {
                    await matchedMessage.DeleteAsync();
                }
            }
        }
    }

    public async Task HandleCommand(SocketSlashCommand command)
    {
        if (!ShouldProcessCommand(command))
        {
            _logger.LogDebug("Command was ignored with content: \"{Message}\"", command.Data.ToString());
            return;
        }

        await command.DeferAsync();
        
        var results = await Match(command);
        
        if (!results.Any())
        {
            return;
        }
        
        foreach (var (site, match) in results)
        {
            _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);
            
            try
            {
                var response = await _sites[site].Process(match);

                if (response is null)
                {
                    _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);
                    continue;
                }

                await _messageManager.Send(command, response);
            }
            catch (Exception ex)
            {
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

    private bool ShouldProcessMessage(SocketUserMessage message)
    {
        if (HasIgnoreMessageTagsInContent(message))
        {
            return false;
        }

        if (!HasPermissionsToCreateEmbed(message))
        {
            return false;
        }

        return true;
    }

    private bool ShouldProcessCommand(SocketSlashCommand command)
    {
        if (!HasPermissionsToCreateEmbed(command))
        {
            return false;
        }

        return true;
    }

    private bool HasIgnoreMessageTagsInContent(SocketUserMessage message)
    {
        return IgnoreContentRegex().IsMatch(message.Content);
    }
    
    private bool HasPermissionsToCreateEmbed(SocketUserMessage message)
    {
        // TODO: Implement checking Guild and Channel permissions to see if I can send messages in the first place
        return true;
    }
    
    private bool HasPermissionsToCreateEmbed(SocketSlashCommand message)
    {
        // TODO: Implement checking Guild and Channel permissions to see if I can send messages in the first place
        return true;
    }
}

public record SiteManagerProcessResult(string Site, Match Match);
