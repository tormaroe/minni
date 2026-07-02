# MinniStore (Minni)

<p align="center">
  <img src="gfx/Minni-owl.png" alt="Minni Logo" width="150" />
</p>

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

Alternatively, you can build and run using `just`:
```bash
just run
```

---

## REST API Reference

### 1. Append Events
Appends one or more events to a stream.

* **Method**: `POST`
* **Route**: `/streams/{aggregateId}`
* **Headers**:
  * `If-Match`: (Optional) Expected version of the stream (integer encapsulated in double-quotes, e.g., `"4"`). Matches the number of events in the stream prior to this append.
* **Request Body**:
  ```json
  {
    "expectedVersion": 4, // Optional alternative to the If-Match header
    "events": [
      {
        "data": "eyJrZXkiOiAidmFsdWUtMSJ9" // Base64-encoded string of event bytes
      }
    ]
  }
  ```
* **Success Response (`201 Created`)**:
  ```json
  {
    "aggregateId": "order-1029",
    "currentVersion": 5
  }
  ```
* **Error Responses**:
  * `409 Conflict`: Concurrency conflict (expected version mismatch).
  * `400 Bad Request`: Missing body, empty events array, invalid base64 data, or conflicting version specification.

---

### 2. Retrieve Stream
Retrieves events from a specific stream.

* **Method**: `GET`
* **Route**: `/streams/{aggregateId}`
* **Query Parameters**:
  * `fromVersion`: (Optional, default = `1`) Read events starting from this sequence number (inclusive).
* **Success Response (`200 OK`)**:
  ```json
  [
    {
      "sequenceNumber": 1,
      "timestamp": 1782930292300,
      "data": "eyJrZXkiOiAidmFsdWUtMSJ9" // Base64-encoded string of event bytes
    }
  ]
  ```
* **Error Responses**:
  * `400 Bad Request`: `fromVersion` is less than 1.

---

## Utility Scripts

We provide helper bash scripts inside the [scripts/](file:///C:/dev/minnistore/scripts) folder for quick testing:

### Post an Event
Appends a JSON payload to a stream:
```bash
./scripts/post-event.sh <aggregate-id> '<json-payload>'
# Example:
./scripts/post-event.sh order-1029 '{"status": "Created", "itemsCount": 3}'
```

### Retrieve Events
Fetches and decodes the stream events into a human-readable format:
```bash
./scripts/get-events.sh <aggregate-id>
# Example:
./scripts/get-events.sh order-1029
```

## License
Licensed under the [MIT License](file:///C:/dev/minnistore/LICENSE).
