using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MinniStore.Storage;

public class StorageEngine : IStorageEngine, IDisposable
{
    private readonly string _dbPath;
    private readonly ILogger<StorageEngine> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ImmutableList<EventIndexEntry>> _index = new();
    
    private FileStream? _fileStream;

    public StorageEngine(CommandLineOptions options, ILogger<StorageEngine> logger)
    {
        _dbPath = options.DbPath;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing storage engine with database file: {DbPath}", _dbPath);

        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Open the file with ReadWrite access and ReadWrite sharing to allow concurrent lock-free reads
        _fileStream = new FileStream(
            _dbPath, 
            FileMode.OpenOrCreate, 
            FileAccess.ReadWrite, 
            FileShare.ReadWrite, 
            bufferSize: 4096, 
            useAsync: true
        );

        if (_fileStream.Length == 0)
        {
            await InitializeNewDatabaseAsync();
        }
        else if (_fileStream.Length < 5)
        {
            _logger.LogWarning("Database file header is incomplete ({Length} bytes). Re-initializing database.", _fileStream.Length);
            _fileStream.SetLength(0);
            await InitializeNewDatabaseAsync();
        }
        else
        {
            await ReadAndVerifyDatabaseAsync();
        }
    }

    private async Task InitializeNewDatabaseAsync()
    {
        _logger.LogInformation("Creating new database header...");
        
        byte[] header = new byte[5];
        // Magic bytes "MNST"
        header[0] = 0x4D;
        header[1] = 0x4E;
        header[2] = 0x53;
        header[3] = 0x54;
        // Version 0x01
        header[4] = 0x01;

        _fileStream!.Position = 0;
        await _fileStream.WriteAsync(header);
        await _fileStream.FlushAsync();
        _fileStream.Flush(true); // Force physical disk flush
    }

    private async Task ReadAndVerifyDatabaseAsync()
    {
        _logger.LogInformation("Verifying existing database and rebuilding index...");

        _fileStream!.Position = 0;
        byte[] header = new byte[5];
        int read = await _fileStream.ReadAsync(header);
        if (read < 5)
        {
            throw new InvalidDataException("Failed to read database file header.");
        }

        // Verify magic bytes "MNST"
        if (header[0] != 0x4D || header[1] != 0x4E || header[2] != 0x53 || header[3] != 0x54)
        {
            throw new InvalidDataException("Database file magic bytes mismatch. Not a valid MinniStore database.");
        }

        // Verify version 0x01
        if (header[4] != 0x01)
        {
            throw new InvalidDataException($"Unsupported database format version: 0x{header[4]:X2}. Supported version is 0x01.");
        }

        long currentOffset = 5;
        long fileLength = _fileStream.Length;

        while (currentOffset < fileLength)
        {
            // 1. Check if there is enough space left for record marker (1 byte) and length (4 bytes)
            if (fileLength - currentOffset < 5)
            {
                _logger.LogWarning("Partial record header detected at offset {Offset}. Truncating database.", currentOffset);
                _fileStream.SetLength(currentOffset);
                break;
            }

            _fileStream.Position = currentOffset;
            byte[] headerBuffer = new byte[5];
            int bytesRead = _fileStream.Read(headerBuffer, 0, 5);
            if (bytesRead < 5)
            {
                _logger.LogWarning("Failed to read record header at offset {Offset}. Truncating database.", currentOffset);
                _fileStream.SetLength(currentOffset);
                break;
            }

            byte marker = headerBuffer[0];
            if (marker != 0xEE)
            {
                _logger.LogWarning("Invalid record marker 0x{Marker:X2} at offset {Offset}. Truncating database.", marker, currentOffset);
                _fileStream.SetLength(currentOffset);
                break;
            }

            int recordLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(1, 4));
            if (recordLength < 0)
            {
                _logger.LogWarning("Negative record length {Length} at offset {Offset}. Truncating database.", recordLength, currentOffset);
                _fileStream.SetLength(currentOffset);
                break;
            }

            // 2. Check if remaining file size is enough for record fields (recordLength) + checksum (4 bytes)
            long neededBytes = (long)recordLength + 4;
            if (fileLength - (currentOffset + 5) < neededBytes)
            {
                _logger.LogWarning("Incomplete record body at offset {Offset}. Expected {Expected} bytes, but only {Actual} left. Truncating database.", 
                    currentOffset, neededBytes, fileLength - (currentOffset + 5));
                _fileStream.SetLength(currentOffset);
                break;
            }

            byte[] recordBodyAndChecksum = new byte[recordLength + 4];
            _fileStream.Position = currentOffset + 5;
            bytesRead = _fileStream.Read(recordBodyAndChecksum, 0, recordBodyAndChecksum.Length);
            if (bytesRead < recordBodyAndChecksum.Length)
            {
                _logger.LogWarning("Failed to read full record body at offset {Offset}. Truncating database.", currentOffset);
                _fileStream.SetLength(currentOffset);
                break;
            }

            // Checksum FNV-1a calculation
            byte[] checksumInput = new byte[5 + recordLength];
            checksumInput[0] = 0xEE;
            BinaryPrimitives.WriteInt32LittleEndian(checksumInput.AsSpan(1, 4), recordLength);
            Array.Copy(recordBodyAndChecksum, 0, checksumInput, 5, recordLength);

            uint computedChecksum = ComputeFnv1a(checksumInput);
            uint storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(recordBodyAndChecksum.AsSpan(recordLength, 4));

            if (computedChecksum != storedChecksum)
            {
                _logger.LogWarning("Checksum mismatch at offset {Offset}. Computed: {Computed:X8}, Stored: {Stored:X8}. Truncating database.", 
                    currentOffset, computedChecksum, storedChecksum);
                _fileStream.SetLength(currentOffset);
                break;
            }

            // Deserialize Aggregate ID & Timestamp
            long timestamp = BinaryPrimitives.ReadInt64LittleEndian(recordBodyAndChecksum.AsSpan(0, 8));
            ushort aggIdLen = BinaryPrimitives.ReadUInt16LittleEndian(recordBodyAndChecksum.AsSpan(8, 2));
            string aggregateId = Encoding.UTF8.GetString(recordBodyAndChecksum, 10, aggIdLen);

            int totalRecordSize = 5 + recordLength + 4;
            var entry = new EventIndexEntry(currentOffset, totalRecordSize, timestamp);
            
            _index.AddOrUpdate(aggregateId,
                _ => [entry],
                (_, list) => list.Add(entry));

            currentOffset += totalRecordSize;
        }

        await _fileStream.FlushAsync();
        _fileStream.Flush(true);
    }

    public Task<IReadOnlyList<EventRecord>> ReadEventsAsync(string aggregateId, int fromVersion = 1)
    {
        if (fromVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), "fromVersion must be greater than or equal to 1.");
        }

        if (_fileStream == null)
        {
            throw new InvalidOperationException("Storage engine is not initialized.");
        }

        if (!_index.TryGetValue(aggregateId, out var entries) || entries.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<EventRecord>>([]);
        }

        if (fromVersion > entries.Count)
        {
            return Task.FromResult<IReadOnlyList<EventRecord>>([]);
        }

        var results = new List<EventRecord>(entries.Count - (fromVersion - 1));
        
        for (int i = fromVersion - 1; i < entries.Count; i++)
        {
            var entry = entries[i];
            
            byte[] recordBuffer = new byte[entry.RecordSize];
            int readBytes = RandomAccess.Read(_fileStream.SafeFileHandle, recordBuffer, entry.FileOffset);
            if (readBytes < entry.RecordSize)
            {
                throw new IOException($"Truncated read: expected to read {entry.RecordSize} bytes from offset {entry.FileOffset}, but read only {readBytes} bytes.");
            }

            long timestamp = BinaryPrimitives.ReadInt64LittleEndian(recordBuffer.AsSpan(5, 8));
            ushort aggIdLen = BinaryPrimitives.ReadUInt16LittleEndian(recordBuffer.AsSpan(13, 2));
            int dataBlobLen = BinaryPrimitives.ReadInt32LittleEndian(recordBuffer.AsSpan(15 + aggIdLen, 4));
            
            byte[] dataBlob = new byte[dataBlobLen];
            Array.Copy(recordBuffer, 19 + aggIdLen, dataBlob, 0, dataBlobLen);

            results.Add(new EventRecord(aggregateId, timestamp, dataBlob));
        }

        return Task.FromResult<IReadOnlyList<EventRecord>>(results);
    }

    public async Task<int> WriteEventsAsync(string aggregateId, IEnumerable<byte[]> events, int? expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            throw new ArgumentException("Aggregate ID cannot be empty.", nameof(aggregateId));
        }
        if (events == null)
        {
            throw new ArgumentNullException(nameof(events));
        }
        if (_fileStream == null)
        {
            throw new InvalidOperationException("Storage engine is not initialized.");
        }

        await _writeLock.WaitAsync();
        long offsetRollback = -1;
        try
        {
            int currentVersion = 0;
            if (_index.TryGetValue(aggregateId, out var existingEntries))
            {
                currentVersion = existingEntries.Count;
            }

            if (expectedVersion.HasValue && currentVersion != expectedVersion.Value)
            {
                throw new ConcurrencyConflictException(aggregateId, expectedVersion.Value, currentVersion);
            }

            offsetRollback = _fileStream.Length;
            _fileStream.Position = offsetRollback;

            var newEntries = new List<EventIndexEntry>();
            long currentRecordOffset = offsetRollback;
            int aggregateIdByteCount = Encoding.UTF8.GetByteCount(aggregateId);

            foreach (var data in events)
            {
                if (data == null)
                {
                    throw new ArgumentException("Event payload data cannot be null.");
                }

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                int recordLength = 8 + 2 + aggregateIdByteCount + 4 + data.Length; // timestamp(8) + aggIdLen(2) + aggId + dataLen(4) + data
                int totalRecordSize = 1 + 4 + recordLength + 4; // Marker(1) + Length(4) + recordLength + Checksum(4)

                byte[] recordBuffer = new byte[totalRecordSize];
                
                recordBuffer[0] = 0xEE;
                BinaryPrimitives.WriteInt32LittleEndian(recordBuffer.AsSpan(1, 4), recordLength);
                BinaryPrimitives.WriteInt64LittleEndian(recordBuffer.AsSpan(5, 8), timestamp);
                BinaryPrimitives.WriteUInt16LittleEndian(recordBuffer.AsSpan(13, 2), (ushort)aggregateIdByteCount);
                Encoding.UTF8.GetBytes(aggregateId, 0, aggregateId.Length, recordBuffer, 15);
                BinaryPrimitives.WriteInt32LittleEndian(recordBuffer.AsSpan(15 + aggregateIdByteCount, 4), data.Length);
                Array.Copy(data, 0, recordBuffer, 19 + aggregateIdByteCount, data.Length);

                uint checksum = ComputeFnv1a(recordBuffer.AsSpan(0, 5 + recordLength));
                BinaryPrimitives.WriteUInt32LittleEndian(recordBuffer.AsSpan(5 + recordLength, 4), checksum);

                await _fileStream.WriteAsync(recordBuffer);

                newEntries.Add(new EventIndexEntry(currentRecordOffset, totalRecordSize, timestamp));
                currentRecordOffset += totalRecordSize;
            }

            await _fileStream.FlushAsync();
            _fileStream.Flush(true);

            _index.AddOrUpdate(aggregateId,
                _ => [.. newEntries],
                (_, list) => [.. list, .. newEntries]);

            return currentVersion + newEntries.Count;
        }
        catch (Exception ex) when (ex is not ConcurrencyConflictException)
        {
            if (offsetRollback != -1 && _fileStream != null)
            {
                try
                {
                    _fileStream.SetLength(offsetRollback);
                    await _fileStream.FlushAsync();
                    _fileStream.Flush(true);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback database file to offset {Offset} after error.", offsetRollback);
                }
            }
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static uint ComputeFnv1a(ReadOnlySpan<byte> data)
    {
        uint hash = 2166136261;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 16777619;
        }
        return hash;
    }

    public void Dispose()
    {
        _fileStream?.Dispose();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
