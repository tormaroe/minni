namespace MinniStore.Storage;

public class ConcurrencyConflictException : Exception
{
    public string AggregateId { get; }
    public int ExpectedVersion { get; }
    public int ActualVersion { get; }

    public ConcurrencyConflictException(string aggregateId, int expectedVersion, int actualVersion)
        : base($"Concurrency conflict on aggregate '{aggregateId}'. Expected version {expectedVersion}, but actual version is {actualVersion}.")
    {
        AggregateId = aggregateId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}
