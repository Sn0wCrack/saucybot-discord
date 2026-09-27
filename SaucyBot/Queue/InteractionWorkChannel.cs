using System.Threading.Channels;
using Discord;
using Discord.WebSocket;

namespace SaucyBot.Queue;

public interface IInteractionWorkItem
{
    ulong Id { get; }
    SocketInteraction? SocketInteraction { get; }
    bool IsSlashCommand { get; }
    string? CommandName { get; }
    bool HasResponded { get; }
    Task DeferAsync(CancellationToken cancellationToken = default);
    Task RespondAsync(
        string content,
        bool ephemeral,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null);
    Task FollowupAsync(
        string content,
        bool ephemeral,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null);
}

public static class InteractionAcknowledgementPolicy
{
    public static bool ShouldDefer(string? commandName) =>
        string.Equals(commandName, "sauce", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldExecuteImmediately(string? commandName) =>
        string.Equals(commandName, "settings", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldDefer(IInteractionWorkItem interaction) =>
        ShouldDefer(interaction.CommandName) && !interaction.HasResponded;

    public static bool ShouldExecuteImmediately(IInteractionWorkItem interaction) =>
        !interaction.HasResponded &&
        (!interaction.IsSlashCommand || ShouldExecuteImmediately(interaction.CommandName));
}

public static class InteractionFailureResponder
{
    public static async Task<bool> SendAsync(
        IInteractionWorkItem interaction,
        ILogger logger,
        TimeSpan timeout,
        CancellationToken processingCancellation = default)
    {
        using var responseCancellation = new CancellationTokenSource(timeout);

        try
        {
            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync(
                    "Failed to process this interaction.",
                    ephemeral: true,
                    responseCancellation.Token,
                    timeout);
            }
            else
            {
                await interaction.RespondAsync(
                    "Failed to process this interaction.",
                    ephemeral: true,
                    responseCancellation.Token,
                    timeout);
            }

            return true;
        }
        catch (OperationCanceledException) when (responseCancellation.IsCancellationRequested)
        {
            logger.LogWarning("Interaction {InteractionId} failure response expired before it could be sent", interaction.Id);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send interaction failure response for {InteractionId}", interaction.Id);
            return false;
        }
    }
}

public sealed class SocketInteractionWorkItem : IInteractionWorkItem
{
    private readonly SocketInteraction _interaction;

    public SocketInteractionWorkItem(SocketInteraction interaction)
    {
        _interaction = interaction;
    }

    public ulong Id => _interaction.Id;
    public SocketInteraction SocketInteraction => _interaction;
    public string? CommandName => (_interaction as SocketSlashCommand)?.CommandName;
    public bool IsSlashCommand => _interaction is SocketSlashCommand;
    public bool HasResponded => _interaction.HasResponded;

    public async Task DeferAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _interaction.DeferAsync(options: new RequestOptions { CancelToken = cancellationToken });
    }

    public Task RespondAsync(
        string content,
        bool ephemeral,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _interaction.RespondAsync(
            content,
            ephemeral: ephemeral,
            options: CreateRequestOptions(cancellationToken, timeout));
    }

    public Task FollowupAsync(
        string content,
        bool ephemeral,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _interaction.FollowupAsync(
            content,
            ephemeral: ephemeral,
            options: CreateRequestOptions(cancellationToken, timeout));
    }

    private static RequestOptions CreateRequestOptions(CancellationToken cancellationToken, TimeSpan? timeout) => new()
    {
        CancelToken = cancellationToken,
        Timeout = timeout is not null ? (int)Math.Ceiling(timeout.Value.TotalMilliseconds) : null
    };
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
