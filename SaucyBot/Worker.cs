using Discord;
using Discord.WebSocket;
using SaucyBot.Library;

namespace SaucyBot;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    
    private readonly SiteManager _siteManager;
    private readonly MessageManager _messageManager;
    
    private DiscordShardedClient? _client;

    private Timer _statusTimer;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        SiteManager siteManager,
        MessageManager messageManager
    ) {
        _logger = logger;
        _configuration = configuration;
        _siteManager = siteManager;
        _messageManager = messageManager;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages
        };

        _client = new DiscordShardedClient(config);
        
        _client.Log += LogAsync;
        _client.MessageReceived += HandleMessageAsync;

        await _client.LoginAsync(TokenType.Bot, _configuration["Bot:DiscordToken"]);
        await _client.StartAsync();
        
        _statusTimer = new Timer(async _ =>
        {
            await _client.SetGameAsync($"Your Links... | Servers: {_client.Guilds.Count}", type: ActivityType.Watching);
        },
        null,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMinutes(1));
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
        await _client.DisposeAsync();
    }

    private async Task HandleMessageAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
        {
            return;
        }

        var results = await _siteManager.Match(message.Content);

        foreach (var (site, match) in results)
        {
            _logger.LogDebug("Matched link \"{Match}\" to site {Site}", match, site);

            var response = await _siteManager.Process(site, match, message);

            if (response == null)
            {
                _logger.LogDebug("Failed to process match \"{Match}\" of site {Site}", match, site);
                continue;
            }

            await _messageManager.Send(message, response);
        }
    }

    private async Task LogAsync(LogMessage message)
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
        await Task.CompletedTask;
    }
}
