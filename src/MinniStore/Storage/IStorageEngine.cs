namespace MinniStore.Storage;

public interface IStorageEngine
{
    Task InitializeAsync();
    Task<int> WriteEventsAsync(string aggregateId, IEnumerable<byte[]> events, int expectedVersion);
    Task<IReadOnlyList<EventRecord>> ReadEventsAsync(string aggregateId, int fromVersion = 1);
}
