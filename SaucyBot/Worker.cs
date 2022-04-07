using Discord;
using Discord.WebSocket;
using SaucyBot.Services;

namespace SaucyBot;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;

    private readonly DatabaseManager _databaseManager;
    private readonly SiteManager _siteManager;
    private readonly MessageManager _messageManager;
    
    private DiscordShardedClient? _client;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        DatabaseManager databaseManager,
        SiteManager siteManager,
        MessageManager messageManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _databaseManager = databaseManager;
        _siteManager = siteManager;
        _messageManager = messageManager;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _databaseManager.EnsureAllMigrationsHaveRun();

        var config = new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
            MessageCacheSize = 50,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
        };

        _client = new DiscordShardedClient(config);
        
        _client.Log += LogAsync;
        _client.MessageReceived += HandleMessageAsync;

        await _client.LoginAsync(TokenType.Bot, _configuration["Bot:DiscordToken"]);
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

        Task.Run(async () =>
        {
            try
            {
                var results = await _siteManager.Match(message);

                foreach (var (site, match) in results)
                {
                    _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);

                    var response = await _siteManager.Process(site, match, message);

                    if (response is null)
                    {
                        _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);
                        continue;
                    }

                    await _messageManager.Send(message, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
            }
        });

        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage message)
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
