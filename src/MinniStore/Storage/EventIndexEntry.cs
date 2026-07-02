namespace MinniStore.Storage;

public record EventIndexEntry(
    long FileOffset,      // Position of record marker in DB file
    int RecordSize,       // Total size of record on disk
    long Timestamp        // Event timestamp
);
