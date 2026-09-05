using System.Threading.Channels;
using Discord.WebSocket;

namespace SaucyBot.Queue;

public interface IInteractionWorkItem
{
    ulong Id { get; }
    SocketInteraction? SocketInteraction { get; }
    Task FollowupAsync(string content, bool ephemeral, CancellationToken cancellationToken = default);
}

public sealed class SocketInteractionWorkItem(SocketInteraction interaction) : IInteractionWorkItem
{
    public ulong Id => interaction.Id;
    public SocketInteraction SocketInteraction => interaction;

    public Task FollowupAsync(string content, bool ephemeral, CancellationToken cancellationToken = default) =>
        FollowupAsyncCore(content, ephemeral, cancellationToken);

    private async Task FollowupAsyncCore(string content, bool ephemeral, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await interaction.FollowupAsync(content, ephemeral: ephemeral);
    }
}

public sealed class InteractionWorkChannel
{
    private readonly Channel<IInteractionWorkItem> _channel;

    public InteractionWorkChannel(WorkQueueOptions options)
    {
        _channel = Channel.CreateBounded<IInteractionWorkItem>(new BoundedChannelOptions(options.InteractionChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public ValueTask WriteAsync(IInteractionWorkItem interaction, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(interaction, cancellationToken);

    public IAsyncEnumerable<IInteractionWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}
