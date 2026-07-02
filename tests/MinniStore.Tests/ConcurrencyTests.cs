using Shouldly;
using MinniStore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace MinniStore.Tests;

public class ConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CommandLineOptions _options;
    private readonly StorageEngine _engine;

    public ConcurrencyTests()
    {
        _dbPath = Path.Combine(Directory.GetCurrentDirectory(), $"test_concurrency_{Guid.NewGuid()}.db");
        _options = new CommandLineOptions { DbPath = _dbPath };
        _engine = new StorageEngine(_options, NullLogger<StorageEngine>.Instance);
    }

    public void Dispose()
    {
        _engine.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch {}
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ConcurrentWriters_OnDifferentStreams_AllSucceed()
    {
        await _engine.InitializeAsync();

        int taskCount = 10;
        int eventsPerTask = 50;

        var tasks = Enumerable.Range(0, taskCount).Select(async taskId =>
        {
            var streamId = $"stream-{taskId}";
            for (int i = 0; i < eventsPerTask; i++)
            {
                var payload = Encoding.UTF8.GetBytes($"event-{i}");
                await _engine.WriteEventsAsync(streamId, [payload], i);
            }
        });

        await Task.WhenAll(tasks);

        // Verify all streams have the expected number of events
        for (int taskId = 0; taskId < taskCount; taskId++)
        {
            var events = await _engine.ReadEventsAsync($"stream-{taskId}");
            events.Count.ShouldBe(eventsPerTask);
            for (int i = 0; i < eventsPerTask; i++)
            {
                Encoding.UTF8.GetString(events[i].Data).ShouldBe($"event-{i}");
            }
        }
    }

    [Fact]
    public async Task ConcurrentWriters_OnSameStream_OnlyOneSucceedsPerVersion()
    {
        await _engine.InitializeAsync();

        string streamId = "shared-stream";
        int totalAppends = 50; // Reduce size slightly to speed up test execution
        int currentVersion = 0;

        var payload = Encoding.UTF8.GetBytes("event-data");

        for (int step = 0; step < totalAppends; step++)
        {
            int competitorsCount = 5;
            var tasks = Enumerable.Range(0, competitorsCount).Select(async _ =>
            {
                try
                {
                    await _engine.WriteEventsAsync(streamId, [payload], currentVersion);
                    return true; // Success
                }
                catch (ConcurrencyConflictException)
                {
                    return false; // Failed concurrency check
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            
            // Exactly one competitor should have succeeded
            results.Count(x => x).ShouldBe(1);
            currentVersion++;
        }

        var events = await _engine.ReadEventsAsync(streamId);
        events.Count.ShouldBe(totalAppends);
    }
}
