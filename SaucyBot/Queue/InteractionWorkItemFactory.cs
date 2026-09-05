using Discord.WebSocket;

namespace SaucyBot.Queue;

public interface IInteractionWorkItemFactory
{
    IInteractionWorkItem Create(SocketInteraction interaction);
}

public sealed class InteractionWorkItemFactory : IInteractionWorkItemFactory
{
    public IInteractionWorkItem Create(SocketInteraction interaction) => new SocketInteractionWorkItem(interaction);
}
