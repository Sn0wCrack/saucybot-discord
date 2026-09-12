using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SaucyBot.Diagnostics;
using SaucyBot.Extensions.Discord;
using SaucyBot.Library;
using SaucyBot.Library.Discord;
using SaucyBot.Options;
using SaucyBot.Queue;
using SaucyBot.Services;
using SaucyBot.Site;

namespace SaucyBot;

public sealed class DiscordClientHost
{
    private readonly ILogger<DiscordClientHost> _logger;
    private readonly BotOptions _botOptions;
    private readonly IMessageWorkQueue _messageWorkQueue;
    private readonly SiteRegistry _siteRegistry;
    private readonly InteractionWorkChannel _interactionWorkChannel;
    private readonly IInteractionProcessor _interactionProcessor;
    private readonly WorkQueueHostedService _workQueueHostedService;
    private readonly ISaucyBotMetrics _metrics;
    private readonly InteractionHandler _interactionHandler;
    private readonly IMessageResolver _messageResolver;

    private BaseSocketClient? _client;

    public DiscordClientHost(
        ILogger<DiscordClientHost> logger,
        IOptions<BotOptions> botOptions,
        IMessageWorkQueue messageWorkQueue,
        SiteRegistry siteRegistry,
        InteractionWorkChannel interactionWorkChannel,
        IInteractionProcessor interactionProcessor,
        WorkQueueHostedService workQueueHostedService,
        ISaucyBotMetrics metrics,
        InteractionHandler interactionHandler,
        IMessageResolver messageResolver
    )
    {
        _logger = logger;
        _botOptions = botOptions.Value;
        _messageWorkQueue = messageWorkQueue;
        _siteRegistry = siteRegistry;
        _interactionWorkChannel = interactionWorkChannel;
        _interactionProcessor = interactionProcessor;
        _workQueueHostedService = workQueueHostedService;
        _metrics = metrics;
        _interactionHandler = interactionHandler;
        _messageResolver = messageResolver;
    }

    public async Task LoginAsync()
    {
        _client = CreateClient();
        _messageResolver.Initialize(_client);

        await _client.LoginAsync(TokenType.Bot, _botOptions.DiscordToken);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.StopAsync().WaitAsync(cancellationToken);
            await _client.DisposeAsync().AsTask().WaitAsync(cancellationToken);
        }
    }

    public Task AdmitMessageAsync(MessageWorkItem item) =>
        _messageWorkQueue.EnqueueAsync(item, _workQueueHostedService.AdmissionToken);

    public async Task AdmitInteractionAsync(IInteractionWorkItem item)
    {
        if (InteractionAcknowledgementPolicy.ShouldExecuteImmediately(item))
        {
            try
            {
                await _interactionProcessor.ProcessAsync(item, _workQueueHostedService.AdmissionToken);
                _metrics.Succeeded.Add(1);
            }
            catch (OperationCanceledException) when (_workQueueHostedService.AdmissionToken.IsCancellationRequested)
            {
                _metrics.Cancelled.Add(1);
                await InteractionFailureResponder.SendAsync(item, _logger, TimeSpan.FromSeconds(1));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Immediate interaction processing failed for {InteractionId}", item.Id);
                _metrics.Failed.Add(1);
                await InteractionFailureResponder.SendAsync(item, _logger, TimeSpan.FromSeconds(1));
            }

            return;
        }

        try
        {
            if (InteractionAcknowledgementPolicy.ShouldDefer(item))
            {
                await item.DeferAsync(_workQueueHostedService.AdmissionToken);
            }

            await _interactionWorkChannel.WriteAsync(item, _workQueueHostedService.AdmissionToken);
            _metrics.Enqueued.Add(1);
            _metrics.QueueDepth.Add(1);
        }
        catch (OperationCanceledException) when (_workQueueHostedService.AdmissionToken.IsCancellationRequested)
        {
            _metrics.Cancelled.Add(1);
            throw;
        }
    }

    private BaseSocketClient CreateClient() =>
        _botOptions.ShardMode.ToLowerInvariant().Trim() switch
        {
            "manual" => SetupSocketClient(),
            _ => SetupShardedSocketClient(),
        };

    private DiscordShardedClient SetupShardedSocketClient()
    {
        var config = new DiscordSocketConfig
        {
            TotalShards = _botOptions.TotalShards,
            GatewayIntents = Constants.RequiredGatewayIntents,
            AuditLogCacheSize = 0,
            MessageCacheSize = _botOptions.MessageCacheSize ?? 10,
            ConnectionTimeout = int.MaxValue,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        int[]? ids = null;

        if (_botOptions.ShardId is not null && _botOptions.TotalShards is not null)
        {
            ids = Enumerable.Range((int)(_botOptions.ShardId * _botOptions.TotalShards), (int)_botOptions.TotalShards).ToArray();
        }

        _logger.LogInformation("Starting in Automatic Sharing Mode with {ShardId} and {TotalShards}", _botOptions.ShardId, _botOptions.TotalShards);

        var client = new DiscordShardedClient(ids, config);

        client.MessageReceived += HandleMessageAsync;
        client.InteractionCreated += HandleInteractionAsync;

        client.Log += HandleLogAsync;
        client.ShardReady += HandleShardReadyAsync;
        client.ShardConnected += HandleShardConnectedAsync;
        client.ShardDisconnected += HandleShardDisconnectedAsync;
        client.ShardLatencyUpdated += HandleShardLatencyUpdated;

        _interactionHandler.Log += HandleLogAsync;
        _interactionHandler.Initialize(client);

        return client;
    }

    private DiscordSocketClient SetupSocketClient()
    {
        var config = new DiscordSocketConfig
        {
            ShardId = _botOptions.ShardId,
            TotalShards = _botOptions.TotalShards,
            GatewayIntents = Constants.RequiredGatewayIntents,
            AuditLogCacheSize = 0,
            MessageCacheSize = _botOptions.MessageCacheSize ?? 100,
            ConnectionTimeout = int.MaxValue,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        _logger.LogInformation("Starting in Manual Mode with {ShardId} and {TotalShards}", _botOptions.ShardId, _botOptions.TotalShards);

        var client = new DiscordSocketClient(config);

        client.MessageReceived += HandleMessageAsync;
        client.InteractionCreated += HandleInteractionAsync;

        client.Log += HandleLogAsync;
        client.Ready += HandleSocketClientReadyAsync;

        _interactionHandler.Log += HandleLogAsync;
        _interactionHandler.Initialize(client);

        return client;
    }

    private Task HandleInteractionAsync(SocketInteraction socketInteraction) =>
        AdmitInteractionAsync(new SocketInteractionWorkItem(socketInteraction));

    private async Task HandleMessageAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
        {
            return;
        }

        // Ignore Messages created by the Bot itself
        if (_client is not null && message.Author.Id == _client.CurrentUser.Id)
        {
            return;
        }

        // Don't put the message on the queue if it has no match.
        // This massively improves queue processing times and sizes by discarding all
        // the junk other messages we don't care about.
        if (!_siteRegistry.HasMatch(message.AllMessageCleanContent()))
        {
            return;
        }

        var item = MessageWorkItem.Create(message);

        if (item is null)
        {
            return;
        }

        await AdmitMessageAsync(item);
    }

    private async Task HandleSocketClientReadyAsync()
    {
        if (_client is not DiscordSocketClient client)
        {
            return;
        }

        _logger.LogInformation("[{Source}] {Message}", $"Shard #{client.ShardId}", "Ready");

        if (client.ShardId == 0)
        {
            await _interactionHandler.RegisterAsync();

            _logger.LogDebug("Created or Updated Interaction Commands");
        }

        await SetClientActivityStatusAsync(client);
    }

    private async Task HandleShardReadyAsync(DiscordSocketClient client)
    {
        _logger.LogInformation("[{Source}] {Message}", $"Shard #{client.ShardId}", "Ready");

        if (client.ShardId == 0)
        {
            await _interactionHandler.RegisterAsync();

            _logger.LogDebug("Created or Updated Interaction Commands");
        }

        await SetClientActivityStatusAsync(client);
    }

    private async Task SetClientActivityStatusAsync(DiscordSocketClient client)
    {
        var statusOptions = _botOptions.Status;

        if (!statusOptions.Enabled)
        {
            return;
        }

        var type = statusOptions.Type;

        if (type is null)
        {
            return;
        }

        await client.SetActivityAsync(
            new Game(statusOptions.Text ?? "", type.Value)
        );
    }

    private async Task HandleShardConnectedAsync(DiscordSocketClient client)
    {
        _logger.LogInformation("[{Source}] {Message}", $"Shard #{client.ShardId}", "Connected");
    }

    private async Task HandleShardLatencyUpdated(int oldLatency, int newLatency, DiscordSocketClient client)
    {
        _logger.LogDebug("[{Source}] {Message}", $"Shard #{client.ShardId}", $"Latency Updated: {oldLatency} -> {newLatency}");
    }

    private async Task HandleShardDisconnectedAsync(Exception exception, DiscordSocketClient client)
    {
        if (exception is GatewayReconnectException)
        {
            _logger.LogInformation("[{Source}] {Message}", $"Shard #{client.ShardId}", "Reconnecting (server requested a reconnect)");
            return;
        }

        _logger.LogError(exception, "[{Source}] {Message}", $"Shard #{client.ShardId}", "Disconnected");
    }

    private Task HandleLogAsync(LogMessage message)
    {
        var severity = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };

        if (message.Exception is GatewayReconnectException)
        {
            _logger.LogInformation("[{Source}] {Message}", message.Source, message.Message ?? message.Exception.Message);
            return Task.CompletedTask;
        }

        _logger.Log(severity, message.Exception, "[{Source}] {Message}", message.Source, message.Message ?? message.Exception?.Message);

        return Task.CompletedTask;
    }
}
