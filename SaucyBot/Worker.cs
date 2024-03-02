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

    private BaseSocketClient? _client;

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

        var shardMode = _configuration.GetSection("Bot:ShardMode").Get<string?>() ?? "Automatic";

        _client = shardMode.ToLowerInvariant() switch
        {
            "automatic" => this.SetupShardedSocketClient(),
            "manual" => this.SetupSocketClient(),
            _ => this.SetupShardedSocketClient(),
        };

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

    private DiscordShardedClient SetupShardedSocketClient()
    {
        var config = new DiscordSocketConfig()
        {
            GatewayIntents = Constants.RequiredGatewayIntents,
            AuditLogCacheSize = 0,
            MessageCacheSize = _configuration.GetSection("Bot:MessageCacheSize").Get<int?>() ?? 100,
            ConnectionTimeout = _configuration.GetSection("Bot:ConnectionTimeout").Get<int?>() ?? 30000,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        var client = new DiscordShardedClient(config);
        
        client.Log += HandleLogAsync;
        client.ShardReady += HandleShardReadyAsync;
        client.MessageReceived += HandleMessageAsync;
        client.SlashCommandExecuted += HandleSlashCommandAsync;

        return client;
    }

    private DiscordSocketClient SetupSocketClient()
    {
        var config = new DiscordSocketConfig()
        {
            ShardId = _configuration.GetSection("Bot:ShardId").Get<int?>() ?? 0,
            GatewayIntents = Constants.RequiredGatewayIntents,
            AuditLogCacheSize = 0,
            MessageCacheSize = _configuration.GetSection("Bot:MessageCacheSize").Get<int?>() ?? 100,
            ConnectionTimeout = _configuration.GetSection("Bot:ConnectionTimeout").Get<int?>() ?? 30000,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        var client = new DiscordSocketClient(config);
        
        client.Log += HandleLogAsync;
        client.Ready += HandleSocketClientReadyAsync;
        client.MessageReceived += HandleMessageAsync;
        client.SlashCommandExecuted += HandleSlashCommandAsync;

        return client;
    }

    private Task HandleSlashCommandAsync(SocketSlashCommand socketSlashCommand)
    {   
        Task.Run(async () => await _siteManager.HandleCommand(socketSlashCommand));
        
        return Task.CompletedTask;
    }

    private Task HandleMessageAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
        {
            return Task.CompletedTask;
        }

        // Ignore Messages created by the Bot itself
        if (socketMessage.Author.Id == _client?.CurrentUser.Id)
        {
            return Task.CompletedTask;
        }

        Task.Run(async () => await _siteManager.HandleMessage(message));

        return Task.CompletedTask;
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
            await CreateSlashCommands(client);
            _logger.LogDebug("Created or Updated Slash Commands");
        }

        var status = _configuration.GetSection("Bot:DiscordStatus:Enabled").Get<bool?>() ?? false;

        if (status)
        {
            ActivityType.TryParse(
                _configuration.GetSection("Bot:DiscordStatus:Type").Get<string?>() ?? "",
                out ActivityType activityType
            );
            
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
            await CreateSlashCommands(client);
            _logger.LogDebug("Created or Updated Slash Commands");
        }

        var status = _configuration.GetSection("Bot:DiscordStatus:Enabled").Get<bool?>() ?? false;

        if (status)
        {
            ActivityType.TryParse(
                _configuration.GetSection("Bot:DiscordStatus:Type").Get<string?>() ?? "",
                out ActivityType activityType
            );
            
            await client.SetActivityAsync(
                new Game(
                    _configuration.GetSection("Bot:DiscordStatus:Text").Get<string?>() ?? "",
                    activityType
                )
            );
        }
    }

    private async Task CreateSlashCommands(DiscordSocketClient client)
    {
        var applicationCommandProperties = new List<ApplicationCommandProperties>();
        
        try
        {
            var sauceCommand = new SlashCommandBuilder();
            sauceCommand.WithName("sauce")
                .WithDescription("Create an embed from the provided URL");

            var sauceOption = new SlashCommandOptionBuilder();
            sauceOption.WithName("url")
                .WithDescription("The URL to create the embed from")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(true);
            
            sauceCommand.AddOption(sauceOption);
            
            applicationCommandProperties.Add(sauceCommand.Build());

            await client.BulkOverwriteGlobalApplicationCommandsAsync(applicationCommandProperties.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create global application commands with message: {Message}", ex.Message);
        }
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
