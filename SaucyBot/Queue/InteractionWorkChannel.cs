using System.Threading.Channels;
using Discord.WebSocket;

namespace SaucyBot.Queue;

public sealed class InteractionWorkChannel
{
    private readonly Channel<SocketInteraction> _channel;

    public InteractionWorkChannel(WorkQueueOptions options)
    {
        _channel = Channel.CreateBounded<SocketInteraction>(new BoundedChannelOptions(options.InteractionChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public ValueTask WriteAsync(SocketInteraction interaction, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(interaction, cancellationToken);

    public IAsyncEnumerable<SocketInteraction> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}
