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
        var config = new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
            MessageCacheSize = 0
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
        Task.Run(async () =>
        {
            if (socketMessage is not SocketUserMessage message)
            {
                return;
            }

            // Ignore Messages from Bots (including this one) and Webhook
            if (socketMessage.Author.IsBot || socketMessage.Author.IsWebhook)
            {
                return;
            }

            var results = await _siteManager.Match(message.Content);

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
