using Discord;
using Discord.WebSocket;
using SaucyBot.Library;
using SaucyBot.Services;

namespace SaucyBot;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;

    private readonly DatabaseManager _databaseManager;
    private readonly SiteManager _siteManager;

    private DiscordShardedClient? _client;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        DatabaseManager databaseManager,
        SiteManager siteManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _databaseManager = databaseManager;
        _siteManager = siteManager;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _databaseManager.EnsureAllMigrationsHaveRun();

        var config = new DiscordSocketConfig()
        {
            GatewayIntents = Constants.RequiredGatewayIntents,
            MessageCacheSize = 50,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        _client = new DiscordShardedClient(config);
        
        _client.Log += HandleLogAsync;
        _client.ShardReady += HandleShardReadyAsync;
        _client.MessageReceived += HandleMessageAsync;

        await _client.LoginAsync(TokenType.Bot, _configuration.GetSection("Bot:DiscordToken").Get<string>());
        await _client.StartAsync();
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.StopAsync();
            await _client.DisposeAsync();
        }
    }

    private Task HandleMessageAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
        {
            return Task.CompletedTask;
        }

        // Ignore Messages from Bots (including this one) and Webhook
        if (socketMessage.Author.IsBot || socketMessage.Author.IsWebhook)
        {
            return Task.CompletedTask;
        }

        Task.Run(async () => await _siteManager.Handle(message));

        return Task.CompletedTask;
    }

    private Task HandleShardReadyAsync(DiscordSocketClient client)
    {
        _logger.LogInformation("[{Source}] {Message}", $"Shard {client.ShardId}", "Ready");

        return Task.CompletedTask;
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
