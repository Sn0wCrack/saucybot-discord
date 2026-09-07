using Microsoft.Extensions.DependencyInjection;

namespace SaucyBot.Queue;

public interface IMessageWorkHandler
{
    Task HandleAsync(MessageWorkItem item, CancellationToken cancellationToken);
}

public interface IWorkItemProcessor
{
    Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken);
}

public sealed class WorkItemProcessor : IWorkItemProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkItemProcessor> _logger;

    public WorkItemProcessor(IServiceScopeFactory scopeFactory, ILogger<WorkItemProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ProcessAsync(QueuedMessageWorkItem item, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IMessageWorkHandler>();

        try
        {
            await handler.HandleAsync(item.Item, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to process message work item {EntryId}", item.EntryId);
            throw;
        }
    }
}
