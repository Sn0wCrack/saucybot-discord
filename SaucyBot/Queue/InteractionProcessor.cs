using SaucyBot.Services;

namespace SaucyBot.Queue;

public interface IInteractionProcessor
{
    Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken);
}

public sealed class InteractionProcessor : IInteractionProcessor
{
    private readonly InteractionHandler _handler;

    public InteractionProcessor(InteractionHandler handler)
    {
        _handler = handler;
    }

    public async Task ProcessAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (interaction.SocketInteraction is null)
        {
            throw new InvalidOperationException("Interaction work item has no socket interaction.");
        }

        await _handler.ExecuteAsync(interaction.SocketInteraction, cancellationToken);
    }
}
