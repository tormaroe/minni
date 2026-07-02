using Shouldly;
using MinniStore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace MinniStore.Tests;

public class StorageEngineTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CommandLineOptions _options;
    private readonly StorageEngine _engine;

    public StorageEngineTests()
    {
        _dbPath = Path.Combine(Directory.GetCurrentDirectory(), $"test_db_{Guid.NewGuid()}.db");
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
    public async Task Initialize_WithNewFile_CreatesHeader()
    {
        await _engine.InitializeAsync();
        _engine.Dispose();
        
        File.Exists(_dbPath).ShouldBeTrue();
        var fileBytes = await File.ReadAllBytesAsync(_dbPath);
        fileBytes.Length.ShouldBe(5);
        fileBytes[0..4].ShouldBe([0x4D, 0x4E, 0x53, 0x54]); // Magic MNST
        fileBytes[4].ShouldBe((byte)0x01); // Version 1
    }

    [Fact]
    public async Task Initialize_WithIncompleteHeader_TruncatesAndRewrites()
    {
        await File.WriteAllBytesAsync(_dbPath, [0x4D, 0x4E, 0x53]); // 3 bytes
        
        await _engine.InitializeAsync();
        _engine.Dispose();
        
        var fileBytes = await File.ReadAllBytesAsync(_dbPath);
        fileBytes.Length.ShouldBe(5);
        fileBytes[0..4].ShouldBe([0x4D, 0x4E, 0x53, 0x54]);
    }

    [Fact]
    public async Task Initialize_WithInvalidMagic_ThrowsInvalidDataException()
    {
        await File.WriteAllBytesAsync(_dbPath, [0x11, 0x22, 0x33, 0x44, 0x01]);
        
        await Should.ThrowAsync<InvalidDataException>(async () => await _engine.InitializeAsync());
    }

    [Fact]
    public async Task Initialize_WithInvalidVersion_ThrowsInvalidDataException()
    {
        await File.WriteAllBytesAsync(_dbPath, [0x4D, 0x4E, 0x53, 0x54, 0x02]);
        
        await Should.ThrowAsync<InvalidDataException>(async () => await _engine.InitializeAsync());
    }

    [Fact]
    public async Task WriteAndRead_Succeeds()
    {
        await _engine.InitializeAsync();
        
        var payload1 = Encoding.UTF8.GetBytes("event-data-1");
        var payload2 = Encoding.UTF8.GetBytes("event-data-2");

        // Write events
        var newVersion = await _engine.WriteEventsAsync("stream-1", [payload1, payload2], 0);
        newVersion.ShouldBe(2);

        // Read events
        var events = await _engine.ReadEventsAsync("stream-1");
        events.Count.ShouldBe(2);
        events[0].AggregateId.ShouldBe("stream-1");
        events[0].Data.ShouldBe(payload1);
        events[0].Timestamp.ShouldBeGreaterThan(0);
        events[1].Data.ShouldBe(payload2);
    }

    [Fact]
    public async Task Write_WithConcurrencyConflict_ThrowsException()
    {
        await _engine.InitializeAsync();
        
        var payload = Encoding.UTF8.GetBytes("data");
        
        // Write event with expected version 0
        await _engine.WriteEventsAsync("stream-1", [payload], 0);

        // Write event with wrong expected version (0 instead of 1)
        await Should.ThrowAsync<ConcurrencyConflictException>(async () => 
            await _engine.WriteEventsAsync("stream-1", [payload], 0)
        );
    }

    [Fact]
    public async Task Read_WithNonExistentStream_ReturnsEmpty()
    {
        await _engine.InitializeAsync();
        var events = await _engine.ReadEventsAsync("non-existent");
        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Read_WithVersionHigherThanCount_ReturnsEmpty()
    {
        await _engine.InitializeAsync();
        var payload = Encoding.UTF8.GetBytes("data");
        await _engine.WriteEventsAsync("stream-1", [payload], 0);

        var events = await _engine.ReadEventsAsync("stream-1", fromVersion: 2);
        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Read_WithInvalidFromVersion_ThrowsArgumentOutOfRangeException()
    {
        await _engine.InitializeAsync();
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => 
            await _engine.ReadEventsAsync("stream-1", fromVersion: 0)
        );
    }

    [Fact]
    public async Task Initialize_WithCorruptedTailChecksum_TruncatesAndRecovers()
    {
        await _engine.InitializeAsync();
        
        var payload = Encoding.UTF8.GetBytes("good-data");
        await _engine.WriteEventsAsync("stream-1", [payload], 0);
        
        _engine.Dispose(); // Close file handle

        // Let's manually corrupt the last 4 bytes (the checksum) of the file
        var fileBytes = await File.ReadAllBytesAsync(_dbPath);
        // Corrupt the checksum bytes at the end
        fileBytes[^1] ^= 0xFF;
        fileBytes[^2] ^= 0xFF;
        await File.WriteAllBytesAsync(_dbPath, fileBytes);

        // Initialize a new storage engine to trigger recovery
        using (var newEngine = new StorageEngine(_options, NullLogger<StorageEngine>.Instance))
        {
            await newEngine.InitializeAsync();
        }

        // The corrupted record should have been truncated, returning database size back to header only (5 bytes)
        var newFileBytes = await File.ReadAllBytesAsync(_dbPath);
        newFileBytes.Length.ShouldBe(5);
        
        // Re-open engine to verify we can't read the events
        using (var verifyEngine = new StorageEngine(_options, NullLogger<StorageEngine>.Instance))
        {
            await verifyEngine.InitializeAsync();
            var events = await verifyEngine.ReadEventsAsync("stream-1");
            events.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Initialize_WithIncompleteRecordHeader_TruncatesAndRecovers()
    {
        await _engine.InitializeAsync();
        var payload = Encoding.UTF8.GetBytes("good-data");
        await _engine.WriteEventsAsync("stream-1", [payload], 0);
        _engine.Dispose();

        // Manually append a partial record header (e.g. 3 bytes)
        var fileBytes = await File.ReadAllBytesAsync(_dbPath);
        var originalLength = fileBytes.Length;
        
        var corruptedBytes = new byte[originalLength + 3];
        Array.Copy(fileBytes, corruptedBytes, originalLength);
        corruptedBytes[^3] = 0xEE; // record marker
        corruptedBytes[^2] = 0x01; // part of length
        corruptedBytes[^1] = 0x00; // part of length
        await File.WriteAllBytesAsync(_dbPath, corruptedBytes);

        using (var newEngine = new StorageEngine(_options, NullLogger<StorageEngine>.Instance))
        {
            await newEngine.InitializeAsync();
        }

        // The file should be truncated to the originalLength (removing the partial record)
        var recoveredBytes = await File.ReadAllBytesAsync(_dbPath);
        recoveredBytes.Length.ShouldBe(originalLength);

        using (var verifyEngine = new StorageEngine(_options, NullLogger<StorageEngine>.Instance))
        {
            await verifyEngine.InitializeAsync();
            var events = await verifyEngine.ReadEventsAsync("stream-1");
            events.Count.ShouldBe(1);
        }
    }
}
