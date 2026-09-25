using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SaucyBot.Queue;

public sealed class MessageDeliveryChannel : IAsyncDisposable
{
    private readonly Channel<WorkDelivery<MessageWorkItem>> _channel;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _completed = new();
    private int _isCompleted;

    public MessageDeliveryChannel(WorkQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.RecoveryHandoffCapacity < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Recovery handoff capacity must be at least 2 for reader and recovery reservations.");
        }

        var capacity = options.RecoveryHandoffCapacity;
        _channel = Channel.CreateBounded<WorkDelivery<MessageWorkItem>>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        _slots = new SemaphoreSlim(capacity, capacity);
    }

    public async ValueTask<DeliveryReservation> ReserveAsync(CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _completed.Token);

        try
        {
            await _slots.WaitAsync(waitCancellation.Token);
        }
        catch (OperationCanceledException) when (
            _completed.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ChannelClosedException();
        }

        if (Volatile.Read(ref _isCompleted) != 0)
        {
            _slots.Release();
            throw new ChannelClosedException();
        }

        return new DeliveryReservation(this);
    }

    public async ValueTask<WorkDelivery<MessageWorkItem>> ReadAsync(CancellationToken cancellationToken)
    {
        var delivery = await _channel.Reader.ReadAsync(cancellationToken);
        _slots.Release();
        return delivery;
    }

    public async IAsyncEnumerable<WorkDelivery<MessageWorkItem>> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var delivery in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            _slots.Release();
            yield return delivery;
        }
    }

    public void Complete(Exception? exception = null)
    {
        if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
        {
            return;
        }

        _completed.Cancel();
        _channel.Writer.TryComplete(exception);
    }

    public async ValueTask DisposeAsync()
    {
        Complete();
        var leaseDisposals = new List<Task>();
        while (_channel.Reader.TryRead(out var delivery))
        {
            _slots.Release();
            leaseDisposals.Add(delivery.Lease.DisposeAsync().AsTask());
        }

        await Task.WhenAll(leaseDisposals);
    }

    private bool TryPublish(WorkDelivery<MessageWorkItem> delivery) =>
        Volatile.Read(ref _isCompleted) == 0 && _channel.Writer.TryWrite(delivery);

    private void ReleaseReservation() => _slots.Release();

    public sealed class DeliveryReservation : IAsyncDisposable
    {
        private readonly MessageDeliveryChannel _owner;
        private int _state;

        internal DeliveryReservation(MessageDeliveryChannel owner) => _owner = owner;

        public bool Publish(WorkDelivery<MessageWorkItem> delivery)
        {
            ArgumentNullException.ThrowIfNull(delivery);
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                throw new InvalidOperationException("A delivery reservation can publish only once.");
            }

            if (_owner.TryPublish(delivery))
            {
                return true;
            }

            Interlocked.Exchange(ref _state, 2);
            _owner.ReleaseReservation();
            return false;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _owner.ReleaseReservation();
            }

            return ValueTask.CompletedTask;
        }
    }
}
