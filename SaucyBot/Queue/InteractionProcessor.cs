using SaucyBot.Services;

namespace SaucyBot.Queue;

public interface IInteractionProcessor
{
    Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken);
}

public sealed class InteractionProcessor : IInteractionProcessor
{
    private readonly InteractionHandler _handler;
    private readonly IServiceScopeFactory _scopeFactory;

    public InteractionProcessor(InteractionHandler handler, IServiceScopeFactory scopeFactory)
    {
        _handler = handler;
        _scopeFactory = scopeFactory;
    }

    public async Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (interaction.SocketInteraction is null)
        {
            throw new InvalidOperationException("Interaction work item has no socket interaction.");
        }

        using var scope = _scopeFactory.CreateScope();
        await _handler.ExecuteAsync(interaction.SocketInteraction, scope.ServiceProvider, cancellationToken);
    }
}
