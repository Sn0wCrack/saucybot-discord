namespace SaucyBot.Queue;

public interface IWorkItemLease : IAsyncDisposable
{
    CancellationToken LostToken { get; }

    Task<LeaseOperationResult> CompleteAsync(CancellationToken cancellationToken);

    Task<LeaseOperationResult> RetryAsync(Exception exception, CancellationToken cancellationToken);
}

public enum LeaseOperationResult
{
    Applied,
    AlreadyApplied,
    LeaseLost,
    OutcomeUnknown,
}
