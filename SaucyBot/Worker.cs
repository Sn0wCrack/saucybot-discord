using SaucyBot.Queue;
using SaucyBot.Services;

namespace SaucyBot;

public sealed class Worker : BackgroundService
{
    private readonly IDatabaseMigrator _databaseMigrator;
    private readonly DiscordClientHost _clientHost;
    private readonly WorkQueueHostedService _workQueueHostedService;

    public Worker(
        IDatabaseMigrator databaseMigrator,
        DiscordClientHost clientHost,
        WorkQueueHostedService workQueueHostedService
    )
    {
        _databaseMigrator = databaseMigrator;
        _clientHost = clientHost;
        _workQueueHostedService = workQueueHostedService;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _databaseMigrator.EnsureAllMigrationsHaveRun();

        await _clientHost.LoginAsync();
    }

    protected override Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _workQueueHostedService.StopIntake();

        await _clientHost.StopAsync(cancellationToken);
    }
}
