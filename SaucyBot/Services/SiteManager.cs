using System.Text.RegularExpressions;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SaucyBot.Database.Models;
using SaucyBot.Extensions;
using SaucyBot.Extensions.Discord;
using SaucyBot.Library.Discord;
using SaucyBot.Options;
using SaucyBot.Queue;
using SaucyBot.Site;

namespace SaucyBot.Services;

public sealed class SiteManager : IMessageWorkHandler
{
    private readonly ILogger<SiteManager> _logger;
    private readonly BotOptions _botOptions;
    private readonly MessageManager _messageManager;
    private readonly IGuildConfigurationManager _guildConfigurationManager;
    private readonly SiteRegistry _siteRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageResolver _messageResolver;

    public SiteManager(
        ILogger<SiteManager> logger,
        IOptions<BotOptions> botOptions,
        MessageManager messageManager,
        IGuildConfigurationManager guildConfigurationManager,
        SiteRegistry siteRegistry,
        IServiceProvider serviceProvider,
        IMessageResolver messageResolver
    )
    {
        _logger = logger;
        _botOptions = botOptions.Value;
        _messageManager = messageManager;
        _guildConfigurationManager = guildConfigurationManager;
        _siteRegistry = siteRegistry;
        _serviceProvider = serviceProvider;
        _messageResolver = messageResolver;
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

        var maximumEmbeds = guildConfiguration?.MaximumEmbeds ?? _botOptions.MaximumEmbeds;

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

    public Task<List<SiteManagerProcessResult>> Match(IMessageContext message, GuildConfiguration? guildConfiguration = null)
    {
        var results = new List<SiteManagerProcessResult>();
        var content = message.CleanContent;
        if (content is null or "")
        {
            return Task.FromResult(results);
        }

        var maximumEmbeds = guildConfiguration?.MaximumEmbeds ?? _botOptions.MaximumEmbeds;
        foreach (var (identifier, site) in _siteRegistry.Sites)
        {
            foreach (Match match in site.Pattern.Matches(content))
            {
                results.Add(new SiteManagerProcessResult(identifier, match));
                if (results.Count >= maximumEmbeds)
                {
                    return Task.FromResult(results);
                }
            }
        }

        return Task.FromResult(results);
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

        var maximumEmbeds = guildConfiguration?.MaximumEmbeds ?? _botOptions.MaximumEmbeds;

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
        var context = new DiscordMessageContext(message, _messageResolver);
        await HandleMessage(context, guildConfiguration, CancellationToken.None, () => message.Channel.EnterTypingState());
    }

    private async Task HandleMessage(
        IMessageContext message,
        GuildConfiguration? guildConfiguration,
        CancellationToken cancellationToken,
        Func<IDisposable?>? typingState = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = MessageValidator.ValidateMessage(message, guildConfiguration);

        if (!validation.Passed)
        {
            _logger.LogDebug("Message was ignored: {Reason} (content: \"{Message}\")",
                validation.Reason, message.AllMessageContent);
            return;
        }

        var results = await Match(message, guildConfiguration);

        if (results.Empty())
        {
            return;
        }

        // Show a typing indicator for live gateway messages only.
        using (typingState?.Invoke())
        {
            foreach (var (site, match) in results)
            {
                _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);

                ProcessResponse? response = null;

                try
                {
                    var request = new ProcessRequest(
                        match,
                        guildConfiguration,
                        Context: new ProcessingContext(
                            NsfwAllowed(message),
                            Message: message,
                            CancellationToken: cancellationToken
                        )
                    );

                    response = await _siteRegistry
                        .Resolve(site, _serviceProvider)
                        .Process(request);

                    if (response is null)
                    {
                        _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);
                        continue;
                    }

                    await SendAndDispose(response, () => _messageManager.Send(message, response, cancellationToken));

                    var target = await message.ResolveMessageAsync(cancellationToken);

                    if (target is not null && MessageValidator.HasPermissionToHideEmbed(message))
                    {
                        await target.ModifyAsync(x => x.Flags = MessageFlags.SuppressEmbeds);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMessage)
                {
                    TimeSpan? messageAge = null;
                    if (message.EnqueuedAt is not null)
                    {
                        messageAge = DateTimeOffset.UtcNow - message.EnqueuedAt.Value;
                    }

                    _logger.LogDebug(
                        "Original message {MessageId} was deleted before the {Site} response could be sent; queued age: {MessageAge}",
                        message.Id,
                        site,
                        messageAge);
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
        var message = new QueuedMessageContext(item, _messageResolver);
        var guildConfiguration = await _guildConfigurationManager.GetByGuildId(item.GuildId);
        await HandleMessage(message, guildConfiguration, cancellationToken);
    }

    public async Task HandleCommand(SocketSlashCommand command)
    {
        var guildConfiguration = await _guildConfigurationManager.GetByChannel(command.Channel);

        var validation = MessageValidator.ValidateCommand(command, guildConfiguration);

        if (!validation.Passed)
        {
            _logger.LogDebug("Command was ignored: {Reason} (content: \"{Message}\")",
                validation.Reason, command.Data.ToString());
            await SendCommandResponse(command, "Failed to process provided URL or do not have correct permissions in Channel");
            return;
        }

        var results = await Match(command, guildConfiguration);

        if (results.Empty())
        {
            await SendCommandResponse(command, "Provided URL cannot be sauced");
            return;
        }

        foreach (var (site, match) in results)
        {
            _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);

            ProcessResponse? response = null;

            try
            {
                response = await _siteRegistry.Resolve(site, _serviceProvider).Process(
                    CreateCommandRequest(match, guildConfiguration, new DiscordCommandContext(command)));

                if (response is null)
                {
                    _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);
                    await SendCommandResponse(command, "Failed to create embed information for provided URL");
                    continue;
                }

                await SendAndDispose(response, () => _messageManager.Send(command, response));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occured processing or sending messages");
                await SendCommandResponse(command, "Failed to create embed information for provided URL");
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

    private static Task SendCommandResponse(SocketSlashCommand command, string content) =>
        command.HasResponded
            ? command.FollowupAsync(content, ephemeral: true)
            : command.RespondAsync(content, ephemeral: true);

    private bool NsfwAllowed(IMessageContext message)
    {
        return NsfwAllowed() || message.IsNsfw;
    }

    private bool NsfwAllowed()
    {
        return !_botOptions.RestrictNSFW;
    }

    internal ProcessRequest CreateCommandRequest(
        Match match,
        GuildConfiguration? guildConfiguration,
        ICommandContext command)
    {
        return new ProcessRequest(
            match,
            guildConfiguration,
            Context: new ProcessingContext(
                NsfwAllowed: NsfwAllowed(),
                Command: command,
                CancellationToken: InteractionCancellationContext.Current
            )
        );
    }
}

public record SiteManagerProcessResult(string Site, Match Match);
