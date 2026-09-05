using Microsoft.Extensions.DependencyInjection;
using SaucyBot.Services;

namespace SaucyBot.Queue;

public interface IInteractionProcessor
{
    Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken);
}

public sealed class InteractionProcessor : IInteractionProcessor
{
    private readonly InteractionHandler _handler;
    private readonly IServiceProvider _services;

    public InteractionProcessor(InteractionHandler handler, IServiceProvider services)
    {
        _handler = handler;
        _services = services;
    }

    public Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (interaction.SocketInteraction is null)
        {
            throw new InvalidOperationException("Interaction work item has no socket interaction.");
        }

        return _handler.ExecuteAsync(interaction.SocketInteraction, _services);
    }
}
