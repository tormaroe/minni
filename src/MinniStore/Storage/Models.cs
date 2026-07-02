using System;

namespace MinniStore.Storage;

public record EventRecord(
    string AggregateId,
    long Timestamp,
    byte[] Data
);

public record EventIndexEntry(
    long FileOffset,      // Position of record marker in DB file
    int RecordSize,       // Total size of record on disk
    long Timestamp        // Event timestamp
);

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
