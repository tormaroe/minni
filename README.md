# MinniStore (Minni)

MinniStore is a lightweight, high-performance event store database management system written in C# (.NET). It stores immutable events in an append-only single-file database on disk and exposes a HTTP REST API for operations.

## Key Features
- **Append-only storage**: Immutable events stored in a custom binary single-file log.
- **In-Memory Indexing**: Fast $O(1)$ stream lookups and concurrency validation.
- **Optimistic Concurrency Control**: Optimistic lock checks on write operations.
- **Instrumentation**: Live metrics tracking rates, totals, and server uptime.

## Documentation
- [Specification](file:///C:/dev/minnistore/spec/specification.md)
- [Implementation Plan](file:///C:/dev/minnistore/spec/implementation-plan.md)

## Usage
Start the database server:
```bash
minni --port 25000 --db databasefile.db
```

Clients can then interact with the database via HTTP endpoints:
- `GET /streams/{aggregateId}` - Retrieve all events for an aggregate.
- `POST /streams/{aggregateId}` - Append one or more events to an aggregate.
- `GET /metrics` - Retrieve server status and performance metrics.

## License
Licensed under the [MIT License](file:///C:/dev/minnistore/LICENSE).
