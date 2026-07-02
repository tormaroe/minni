namespace MinniStore.Storage;

public record EventRecord(
    string AggregateId,
    long Timestamp,
    byte[] Data
);
