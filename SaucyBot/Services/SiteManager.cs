using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using SaucyBot.Database.Models;
using SaucyBot.Extensions;
using SaucyBot.Extensions.Discord;
using SaucyBot.Queue;
using SaucyBot.Site;

namespace SaucyBot.Services;

public sealed class SiteManager : IMessageWorkHandler
{
    private readonly ILogger<SiteManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly MessageManager _messageManager;
    private readonly IGuildConfigurationManager _guildConfigurationManager;
    private readonly SiteRegistry _siteRegistry;

    public SiteManager(
        ILogger<SiteManager> logger,
        IConfiguration configuration,
        MessageManager messageManager,
        IGuildConfigurationManager guildConfigurationManager,
        SiteRegistry siteRegistry
    )
    {
        _logger = logger;
        _configuration = configuration;
        _messageManager = messageManager;
        _guildConfigurationManager = guildConfigurationManager;
        _siteRegistry = siteRegistry;
    }

    internal static async Task SendAndDispose(ProcessResponse response, Func<Task> send)
    {
        try
        {
            await send();
        }
        finally
        {
            await response.DisposeAsync();
        }
    }

    public async Task<List<SiteManagerProcessResult>> Match(SocketUserMessage message, GuildConfiguration? guildConfiguration = null)
    {
        var results = new List<SiteManagerProcessResult>();

        var embedCount = 0u;

        var content = message.AllMessageCleanContent();

        if (content is null or "")
        {
            return results;
        }

        var maximumEmbeds = guildConfiguration?.MaximumEmbeds ?? _configuration.GetSection("Bot:MaximumEmbeds").Get<uint>();

        foreach (var (identifier, site) in _siteRegistry.Sites)
        {
            var matches = site.Pattern.Matches(content);

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

    public async Task<List<SiteManagerProcessResult>> Match(SocketSlashCommand command, GuildConfiguration? guildConfiguration = null)
    {
        var results = new List<SiteManagerProcessResult>();

        var embedCount = 0u;

        var content = (string?)command.Data.Options.First().Value;

        if (content is null)
        {
            return results;
        }

        var maximumEmbeds = guildConfiguration?.MaximumEmbeds ?? _configuration.GetSection("Bot:MaximumEmbeds").Get<uint>();

        foreach (var (identifier, site) in _siteRegistry.Sites)
        {
            var matches = site.Pattern.Matches(content);

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
        var guildConfiguration = await _guildConfigurationManager.GetByChannel(message.Channel);

        var validation = MessageValidator.ValidateMessage(message, guildConfiguration);

        if (!validation.Passed)
        {
            _logger.LogDebug("Message was ignored: {Reason} (content: \"{Message}\")",
                validation.Reason, message.AllMessageContent());
            return;
        }

        var results = await Match(message, guildConfiguration);

        if (results.Empty())
        {
            return;
        }

        // Show a "typing..." indicator in the channel for as long as we are processing matches. It is broadcast until the returned object is disposed of, and Discord clears it once we send our reply.
        using (message.Channel.EnterTypingState())
        {
            foreach (var (site, match) in results)
            {
                _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);

                ProcessResponse? response = null;

                try
                {
                    response = await _siteRegistry[site].Process(new ProcessRequest(match, guildConfiguration, message));

                    if (response is null)
                    {
                        _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);
                        continue;
                    }

                    await SendAndDispose(response, () => _messageManager.Send(message, response));

                    if (MessageValidator.HasPermissionToHideEmbed(message))
                    {
                        await message.ModifyAsync(x => x.Flags = MessageFlags.SuppressEmbeds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception occured processing or sending messages");
                }
                finally
                {
                    if (response is not null)
                    {
                        await response.DisposeAsync();
                    }
                }
            }
        }
    }

    public async Task HandleAsync(MessageWorkItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = await _guildConfigurationManager.GetByGuildId(item.GuildId);
        _logger.LogDebug("Queued message {MessageId} is ready for processing", item.MessageId);
    }

    public async Task HandleCommand(SocketSlashCommand command)
    {
        var guildConfiguration = await _guildConfigurationManager.GetByChannel(command.Channel);

        var validation = MessageValidator.ValidateCommand(command, guildConfiguration);

        if (!validation.Passed)
        {
            _logger.LogDebug("Command was ignored: {Reason} (content: \"{Message}\")",
                validation.Reason, command.Data.ToString());
            await command.FollowupAsync("Failed to process provided URL or do not have correct permissions in Channel", ephemeral: true);
            return;
        }

        var results = await Match(command, guildConfiguration);

        if (results.Empty())
        {
            await command.FollowupAsync("Provided URL cannot be sauced", ephemeral: true);
            return;
        }

        foreach (var (site, match) in results)
        {
            _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);

            ProcessResponse? response = null;

            try
            {
                response = await _siteRegistry[site].Process(new ProcessRequest(match, guildConfiguration, Command: command));

                if (response is null)
                {
                    _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);
                    await command.FollowupAsync("Failed to create embed information for provided URL", ephemeral: true);
                    continue;
                }

                await SendAndDispose(response, () => _messageManager.Send(command, response));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occured processing or sending messages");
                await command.FollowupAsync("Failed to create embed information for provided URL", ephemeral: true);
            }
            finally
            {
                if (response is not null)
                {
                    await response.DisposeAsync();
                }
            }
        }
    }
}

public record SiteManagerProcessResult(string Site, Match Match);
