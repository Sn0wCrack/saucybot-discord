using Discord.WebSocket;

namespace SaucyBot.Queue;

public interface IInteractionDeferrer
{
    Task DeferAsync(SocketInteraction interaction);
}

public sealed class InteractionDeferrer : IInteractionDeferrer
{
    public Task DeferAsync(SocketInteraction interaction) => interaction.DeferAsync();
}
