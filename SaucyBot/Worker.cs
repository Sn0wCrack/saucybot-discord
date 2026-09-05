using Discord;
using Discord.WebSocket;
using SaucyBot.Diagnostics;
using SaucyBot.Library;
using SaucyBot.Queue;
using SaucyBot.Services;
using SaucyBot.Site;

namespace SaucyBot;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMessageWorkQueue _messageWorkQueue;
    private readonly InteractionWorkChannel _interactionWorkChannel;
    private readonly WorkQueueHostedService _workQueueHostedService;
    private readonly SaucyBotMetrics? _metrics;
    private readonly Func<SocketMessage, MessageWorkItem?> _messageWorkItemFactory;
    private readonly Func<SocketInteraction, IInteractionWorkItem> _interactionWorkItemFactory;
    private readonly Func<SocketInteraction, Task> _interactionDeferrer;

    private readonly IDatabaseMigrator _databaseMigrator;

    private readonly InteractionHandler _interactionHandler;
    private readonly DiscordMessageResolver? _messageResolver;

    private BaseSocketClient? _client;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IDatabaseMigrator databaseMigrator,
        InteractionHandler interactionHandler,
        IMessageWorkQueue messageWorkQueue,
        InteractionWorkChannel interactionWorkChannel,
        WorkQueueHostedService workQueueHostedService,
        SaucyBotMetrics? metrics = null,
        Func<SocketMessage, MessageWorkItem?>? messageWorkItemFactory = null,
        Func<SocketInteraction, IInteractionWorkItem>? interactionWorkItemFactory = null,
        Func<SocketInteraction, Task>? interactionDeferrer = null,
        DiscordMessageResolver? messageResolver = null
    )
    {
        _logger = logger;
        _configuration = configuration;
        _databaseMigrator = databaseMigrator;
        _interactionHandler = interactionHandler;
        _messageResolver = messageResolver;
        _messageWorkQueue = messageWorkQueue;
        _interactionWorkChannel = interactionWorkChannel;
        _workQueueHostedService = workQueueHostedService;
        _metrics = metrics;
        _messageWorkItemFactory = messageWorkItemFactory ?? (message =>
            message is SocketUserMessage userMessage ? CreateMessageWorkItem(userMessage) : null);
        _interactionWorkItemFactory = interactionWorkItemFactory ?? (interaction => new SocketInteractionWorkItem(interaction));
        _interactionDeferrer = interactionDeferrer ?? (interaction => interaction.DeferAsync());
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

        _messageResolver?.Initialize(_client);

        await _client.LoginAsync(TokenType.Bot, _configuration.GetSection("Bot:DiscordToken").Get<string>());
        await _client.StartAsync();
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _workQueueHostedService.StopIntake();

        if (_client is not null)
        {
            await _client.StopAsync();
            await _client.DisposeAsync();
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
            MessageCacheSize = _configuration.GetSection("Bot:MessageCacheSize").Get<int?>() ?? 100,
            ConnectionTimeout = _configuration.GetSection("Bot:ConnectionTimeout").Get<int?>() ?? 30000,
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

    internal Task HandleInteractionAsync(SocketInteraction socketInteraction) =>
        HandleInteractionAsync(socketInteraction, _interactionDeferrer);

    internal async Task HandleInteractionAsync(
        SocketInteraction socketInteraction,
        Func<SocketInteraction, Task> defer)
    {
        await AdmitInteractionAsync(
            _interactionWorkItemFactory(socketInteraction),
            () => defer(socketInteraction));
    }

    internal async Task HandleMessageAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage)
        {
            return;
        }

        // Ignore Messages created by the Bot itself
        if (_client is not null && socketMessage.Author.Id == _client.CurrentUser.Id)
        {
            return;
        }

        var item = _messageWorkItemFactory(socketMessage);
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

    internal async Task AdmitInteractionAsync(IInteractionWorkItem item, Func<Task> defer)
    {
        await defer();
        try
        {
            await _interactionWorkChannel.WriteAsync(item, _workQueueHostedService.AdmissionToken);
            _metrics?.Enqueued.Add(1);
            _metrics?.QueueDepth.Add(1);
        }
        catch (OperationCanceledException) when (_workQueueHostedService.AdmissionToken.IsCancellationRequested)
        {
            _metrics?.Cancelled.Add(1);
            throw;
        }
    }

    private static MessageWorkItem CreateMessageWorkItem(SocketUserMessage message)
    {
        var guildChannel = message.Channel as SocketGuildChannel;
        var permissions = guildChannel?.Guild.CurrentUser.GetPermissions(guildChannel);
        return new MessageWorkItem(
            message.Id,
            guildChannel?.Guild.Id ?? 0,
            message.Channel.Id,
            message.Author.Id,
            (message.Author as SocketGuildUser)?.Roles.Select(role => role.Id).ToArray() ?? [],
            message.Content ?? "",
            null,
            message.Embeds.Select(embed => new MessageEmbed(embed.Title, embed.Description, embed.Url)).ToArray(),
            permissions?.Has(ChannelPermission.EmbedLinks) ?? false,
            permissions?.Has(ChannelPermission.ManageMessages) ?? false,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
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

        var status = _configuration.GetSection("Bot:DiscordStatus:Enabled").Get<bool?>() ?? false;

        var parsed = Enum.TryParse(
            _configuration.GetSection("Bot:DiscordStatus:Type").Get<string?>() ?? "",
            out ActivityType activityType
        );

        if (status && parsed)
        {
            await _client.SetActivityAsync(
                new Game(
                    _configuration.GetSection("Bot:DiscordStatus:Text").Get<string?>() ?? "",
                    activityType
                )
            );
        }
    }

    private async Task HandleShardReadyAsync(DiscordSocketClient client)
    {
        _logger.LogInformation("[{Source}] {Message}", $"Shard #{client.ShardId}", "Ready");

        if (client.ShardId == 0)
        {
            await _interactionHandler.RegisterAsync();

            _logger.LogDebug("Created or Updated Interaction Commands");
        }

        var status = _configuration.GetSection("Bot:DiscordStatus:Enabled").Get<bool?>() ?? false;

        var parsed = Enum.TryParse(
            _configuration.GetSection("Bot:DiscordStatus:Type").Get<string?>() ?? "",
            out ActivityType activityType
        );

        if (status && parsed)
        {
            await client.SetActivityAsync(
                new Game(
                    _configuration.GetSection("Bot:DiscordStatus:Text").Get<string?>() ?? "",
                    activityType
                )
            );
        }
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
