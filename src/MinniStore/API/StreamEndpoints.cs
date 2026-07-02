using System.Text.Json.Serialization;
using MinniStore.Storage;

namespace MinniStore.API;

public static class StreamEndpoints
{
    public static void MapStreamEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/streams/{aggregateId}", AppendEventsAsync);
        routes.MapGet("/streams/{aggregateId}", GetStreamAsync);
    }

    private static async Task<IResult> AppendEventsAsync(
        string aggregateId,
        HttpContext context,
        IStorageEngine storageEngine)
    {
        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            return Results.BadRequest("Aggregate ID cannot be empty.");
        }

        int? expectedVersion = null;

        // 1. Parse If-Match header if present
        if (context.Request.Headers.TryGetValue("If-Match", out var ifMatchValues))
        {
            var ifMatchStr = ifMatchValues.ToString().Trim('\"', ' ');
            if (int.TryParse(ifMatchStr, out var ver))
            {
                expectedVersion = ver;
            }
            else
            {
                return Results.BadRequest("Invalid If-Match header value. Must be an integer version.");
            }
        }

        // 2. Parse request body
        AppendRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<AppendRequest>();
        }
        catch
        {
            return Results.BadRequest("Malformed request body.");
        }

        if (request == null || request.Events == null || request.Events.Count == 0)
        {
            return Results.BadRequest("Request body must contain a list of events.");
        }

        // 3. Resolve expectedVersion from request body and check for conflicts
        if (request.ExpectedVersion.HasValue)
        {
            if (expectedVersion.HasValue && expectedVersion.Value != request.ExpectedVersion.Value)
            {
                return Results.BadRequest("Version specified in If-Match header conflicts with version in request body.");
            }
            expectedVersion = request.ExpectedVersion.Value;
        }

        // 4. Validate and decode events
        var decodedEvents = new List<byte[]>();
        foreach (var ev in request.Events)
        {
            if (string.IsNullOrEmpty(ev.Data))
            {
                return Results.BadRequest("Event data cannot be empty.");
            }
            try
            {
                var bytes = Convert.FromBase64String(ev.Data);
                decodedEvents.Add(bytes);
            }
            catch (FormatException)
            {
                return Results.BadRequest($"Invalid base64 string in event: '{ev.Data}'.");
            }
        }

        // 5. Write to storage engine
        try
        {
            var newVersion = await storageEngine.WriteEventsAsync(aggregateId, decodedEvents, expectedVersion);
            return Results.Created($"/streams/{aggregateId}", new AppendResponseDto
            {
                AggregateId = aggregateId,
                CurrentVersion = newVersion
            });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static async Task<IResult> GetStreamAsync(
        string aggregateId,
        IStorageEngine storageEngine,
        int fromVersion = 1)
    {
        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            return Results.BadRequest("Aggregate ID cannot be empty.");
        }
        if (fromVersion < 1)
        {
            return Results.BadRequest("fromVersion must be greater than or equal to 1.");
        }

        var events = await storageEngine.ReadEventsAsync(aggregateId, fromVersion);

        var response = events.Select((ev, index) => new EventRecordResponseDto
        {
            SequenceNumber = fromVersion + index,
            Timestamp = ev.Timestamp,
            Data = Convert.ToBase64String(ev.Data)
        }).ToList();

        return Results.Ok(response);
    }
}

public class AppendRequest
{
    [JsonPropertyName("expectedVersion")]
    public int? ExpectedVersion { get; set; }

    [JsonPropertyName("events")]
    public List<EventDataDto>? Events { get; set; }
}

public class EventDataDto
{
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

public class AppendResponseDto
{
    [JsonPropertyName("aggregateId")]
    public string AggregateId { get; set; } = string.Empty;

    [JsonPropertyName("currentVersion")]
    public int CurrentVersion { get; set; }
}

public class EventRecordResponseDto
{
    [JsonPropertyName("sequenceNumber")]
    public int SequenceNumber { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
}
