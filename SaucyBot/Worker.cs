using Discord;
using Discord.WebSocket;
using SaucyBot.Diagnostics;
using SaucyBot.Extensions.Discord;
using SaucyBot.Library;
using SaucyBot.Library.Discord;
using SaucyBot.Queue;
using SaucyBot.Services;
using SaucyBot.Site;

namespace SaucyBot;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMessageWorkQueue _messageWorkQueue;
    private readonly SiteRegistry _siteRegistry;
    private readonly InteractionWorkChannel _interactionWorkChannel;
    private readonly IInteractionProcessor _interactionProcessor;
    private readonly WorkQueueHostedService _workQueueHostedService;
    private readonly ISaucyBotMetrics _metrics;

    private readonly IDatabaseMigrator _databaseMigrator;

    private readonly InteractionHandler _interactionHandler;
    private readonly IMessageResolver _messageResolver;

    private BaseSocketClient? _client;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IDatabaseMigrator databaseMigrator,
        InteractionHandler interactionHandler,
        IMessageWorkQueue messageWorkQueue,
        SiteRegistry siteRegistry,
        InteractionWorkChannel interactionWorkChannel,
        IInteractionProcessor interactionProcessor,
        WorkQueueHostedService workQueueHostedService,
        ISaucyBotMetrics metrics,
        IMessageResolver messageResolver
    )
    {
        _logger = logger;
        _configuration = configuration;
        _databaseMigrator = databaseMigrator;
        _interactionHandler = interactionHandler;
        _messageResolver = messageResolver;
        _messageWorkQueue = messageWorkQueue;
        _siteRegistry = siteRegistry;
        _interactionWorkChannel = interactionWorkChannel;
        _interactionProcessor = interactionProcessor;
        _workQueueHostedService = workQueueHostedService;
        _metrics = metrics;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _databaseMigrator.EnsureAllMigrationsHaveRun();

        var shardMode = _configuration.GetSection("Bot:ShardMode").Get<string?>() ?? "Automatic";

        _client = shardMode.ToLowerInvariant().Trim() switch
        {
            "automatic" => this.SetupShardedSocketClient(),
            "manual" => this.SetupSocketClient(),
            _ => this.SetupShardedSocketClient(),
        };

        _messageResolver.Initialize(_client);

        await _client.LoginAsync(TokenType.Bot, _configuration.GetSection("Bot:DiscordToken").Get<string>());
        await _client.StartAsync();
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _workQueueHostedService.StopIntake();

        if (_client is not null)
        {
            await _client.StopAsync().WaitAsync(cancellationToken);
            await _client.DisposeAsync().AsTask().WaitAsync(cancellationToken);
        }
    }

    private DiscordShardedClient SetupShardedSocketClient()
    {
        var shardId = _configuration.GetSection("Bot:ShardId").Get<int?>();
        var totalShards = _configuration.GetSection("Bot:TotalShards").Get<int?>();

        var config = new DiscordSocketConfig
        {
            TotalShards = totalShards,
            GatewayIntents = Constants.RequiredGatewayIntents,
            AuditLogCacheSize = 0,
            MessageCacheSize = _configuration.GetSection("Bot:MessageCacheSize").Get<int?>() ?? 10,
            ConnectionTimeout = int.MaxValue,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        int[]? ids = null;

        if (shardId is not null && totalShards is not null)
        {
            ids = Enumerable.Range((int)(shardId * totalShards), (int)totalShards).ToArray();
        }

        _logger.LogInformation("Starting in Automatic Sharing Mode with {ShardId} and {TotalShards}", shardId, totalShards);

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
        var shardId = _configuration.GetSection("Bot:ShardId").Get<int?>();
        var totalShards = _configuration.GetSection("Bot:TotalShards").Get<int?>();

        var config = new DiscordSocketConfig
        {
            ShardId = shardId,
            TotalShards = totalShards,
            GatewayIntents = Constants.RequiredGatewayIntents,
            AuditLogCacheSize = 0,
            MessageCacheSize = _configuration.GetSection("Bot:MessageCacheSize").Get<int?>() ?? 100,
            ConnectionTimeout = int.MaxValue,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        _logger.LogInformation("Starting in Manual Mode with {ShardId} and {TotalShards}", shardId, totalShards);

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

    internal async Task AdmitMessageAsync(MessageWorkItem item)
    {
        await _messageWorkQueue.EnqueueAsync(item, _workQueueHostedService.AdmissionToken);
    }

    internal async Task AdmitInteractionAsync(IInteractionWorkItem item)
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
        var status = _configuration.GetSection("Bot:DiscordStatus:Enabled").Get<bool?>() ?? false;

        if (!status)
        {
            return;
        }

        var type = _configuration.GetSection("Bot:DiscordStatus:Type").Get<ActivityType?>();

        if (type is null)
        {
            return;
        }

        await client.SetActivityAsync(
            new Game(_configuration.GetSection("Bot:DiscordStatus:Text").Get<string?>() ?? "", type.Value)
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
        _logger.LogError(exception, "[{Source}] {Message}", "Shard #{client.ShardId}", "Disconnected");
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

        _logger.Log(severity, message.Exception, "[{Source}] {Message}", message.Source, message.Message);

        return Task.CompletedTask;
    }
}
