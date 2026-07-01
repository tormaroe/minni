# MinniStore specification

Minni (or MinniStore) is a event store database (management system). It is built to be simple yet highly performant.

## Key characteristics

- The management system is implemented in C# (.NET)
- The database is stored as a single file on disk
- The database only stores immutable events (conceptually it is append only)
- An event relates to an aggregate, and has an AggregateID (a string value, often a uuid).
- An event has a timestamp (this is basically a version number or sequence number for an aggregate).
- An event has a data blob. The client system is responsible for serializing events using a format of their own choosing.
- The events from a collection that belongs to a single aggregate is referred to as a stream, the aggregate stream.
- Database writes are atomic, reads have optimistic concurrency. Reading your own writes is guaranteed.

## Operations

The database basically only supports two operations:

1. Retrieve all events for an AggregateID in the order they were inserted
2. Insert one or more events for an AggregateID

## Instrumentation

The database process also expose some status information, like:

- Number of events
- Writes pr second metric
- Reads pr second metric
- Number of client connections

## Usage

Start the database server:

```bash
$ minni --port 25000 \
        --db databasefile.db
```

Clients can then issue http requests to post or get events.
