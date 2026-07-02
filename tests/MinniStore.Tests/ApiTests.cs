using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using MinniStore.API;

namespace MinniStore.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AppendAndGetEvents_Succeeds()
    {
        var client = _factory.CreateClient();
        var streamId = $"api-stream-{Guid.NewGuid()}";

        // 1. Append event 1 (ExpectedVersion = 0)
        var payload1 = "eyJrZXkiOiAidmFsdWUtMSJ9"; // base64 for {"key": "value-1"}
        var request1 = new AppendRequest
        {
            ExpectedVersion = 0,
            Events = [new EventDataDto { Data = payload1 }]
        };
        var postResponse1 = await client.PostAsJsonAsync($"/streams/{streamId}", request1);
        postResponse1.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var body1 = await postResponse1.Content.ReadFromJsonAsync<AppendResponseDto>();
        body1.ShouldNotBeNull();
        body1.AggregateId.ShouldBe(streamId);
        body1.CurrentVersion.ShouldBe(1);

        // 2. Append event 2 (ExpectedVersion = 1) using If-Match header
        var payload2 = "eyJrZXkiOiAidmFsdWUtMiJ9"; // base64 for {"key": "value-2"}
        var request2 = new AppendRequest
        {
            Events = [new EventDataDto { Data = payload2 }]
        };
        var message = new HttpRequestMessage(HttpMethod.Post, $"/streams/{streamId}")
        {
            Content = JsonContent.Create(request2)
        };
        message.Headers.Add("If-Match", "\"1\"");
        
        var postResponse2 = await client.SendAsync(message);
        postResponse2.StatusCode.ShouldBe(HttpStatusCode.Created);
        
        var body2 = await postResponse2.Content.ReadFromJsonAsync<AppendResponseDto>();
        body2.ShouldNotBeNull();
        body2.CurrentVersion.ShouldBe(2);

        // 3. Get stream events
        var getResponse = await client.GetAsync($"/streams/{streamId}");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var events = await getResponse.Content.ReadFromJsonAsync<List<EventRecordResponseDto>>();
        events.ShouldNotBeNull();
        events.Count.ShouldBe(2);
        
        events[0].SequenceNumber.ShouldBe(1);
        events[0].Data.ShouldBe(payload1);
        events[0].Timestamp.ShouldBeGreaterThan(0);
        
        events[1].SequenceNumber.ShouldBe(2);
        events[1].Data.ShouldBe(payload2);
        events[1].Timestamp.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AppendEvents_WithConcurrencyConflict_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var streamId = $"api-stream-{Guid.NewGuid()}";

        // Append first event
        var payload = "eyJrZXkiOiAidmFsdWUtMSJ9";
        var request = new AppendRequest
        {
            ExpectedVersion = 0,
            Events = [new EventDataDto { Data = payload }]
        };
        var postResponse1 = await client.PostAsJsonAsync($"/streams/{streamId}", request);
        postResponse1.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Try to append another event with mismatched version (0 instead of 1)
        var conflictRequest = new AppendRequest
        {
            ExpectedVersion = 0,
            Events = [new EventDataDto { Data = payload }]
        };
        var postResponse2 = await client.PostAsJsonAsync($"/streams/{streamId}", conflictRequest);
        postResponse2.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AppendEvents_WithConflictingHeaders_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var streamId = $"api-stream-{Guid.NewGuid()}";

        var payload = "eyJrZXkiOiAidmFsdWUtMSJ9";
        var request = new AppendRequest
        {
            ExpectedVersion = 0, // Version 0 in body
            Events = [new EventDataDto { Data = payload }]
        };

        var message = new HttpRequestMessage(HttpMethod.Post, $"/streams/{streamId}")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("If-Match", "\"1\""); // Version 1 in header -> Conflict!

        var response = await client.SendAsync(message);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("conflicts with version in request body");
    }

    [Fact]
    public async Task AppendEvents_WithInvalidIfMatchHeader_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var streamId = $"api-stream-{Guid.NewGuid()}";

        var payload = "eyJrZXkiOiAidmFsdWUtMSJ9";
        var request = new AppendRequest
        {
            Events = [new EventDataDto { Data = payload }]
        };

        var message = new HttpRequestMessage(HttpMethod.Post, $"/streams/{streamId}")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("If-Match", "\"not-a-number\"");

        var response = await client.SendAsync(message);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("Invalid If-Match header value");
    }

    [Fact]
    public async Task AppendEvents_WithInvalidBase64_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var streamId = $"api-stream-{Guid.NewGuid()}";

        var request = new AppendRequest
        {
            Events = [new EventDataDto { Data = "not-base-64!" }]
        };

        var response = await client.PostAsJsonAsync($"/streams/{streamId}", request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("Invalid base64 string");
    }

    [Fact]
    public async Task AppendEvents_WithEmptyData_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var streamId = $"api-stream-{Guid.NewGuid()}";

        var request = new AppendRequest
        {
            Events = [new EventDataDto { Data = "" }]
        };

        var response = await client.PostAsJsonAsync($"/streams/{streamId}", request);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("Event data cannot be empty");
    }

    [Fact]
    public async Task GetStream_WithNonExistentStream_ReturnsEmptyList()
    {
        var client = _factory.CreateClient();
        var streamId = $"non-existent-{Guid.NewGuid()}";

        var response = await client.GetAsync($"/streams/{streamId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        var events = await response.Content.ReadFromJsonAsync<List<EventRecordResponseDto>>();
        events.ShouldNotBeNull();
        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetStream_WithInvalidFromVersion_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var streamId = $"stream-{Guid.NewGuid()}";

        var response = await client.GetAsync($"/streams/{streamId}?fromVersion=0");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
