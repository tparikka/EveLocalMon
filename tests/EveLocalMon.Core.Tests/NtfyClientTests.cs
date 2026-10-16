using System.Net;
using System.Net.Http.Headers;
using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Net;

namespace EveLocalMon.Core.Tests;

/// <summary>No test here touches the network — the real ntfy.sh is only ever exercised by the
/// user pressing Test, with their own topic.</summary>
public class NtfyClientTests
{
    private static readonly AlertMessage Threat = new(
        AlertKind.ThreatIncreased, "EVE: hostile in Local", "1 red — 1 threat visible", AlertPriority.Urgent, DateTimeOffset.UtcNow);

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(Uri? Uri, string Body, HttpRequestHeaders Headers)> Requests { get; } = [];
        public Func<int, HttpResponseMessage>? Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri, body, request.Headers));
            return Responder?.Invoke(Requests.Count) ?? new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static string? Header(HttpRequestHeaders headers, string name) =>
        headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;

    [Fact]
    public async Task PostsTheMessageToServerSlashTopic()
    {
        var handler = new StubHandler();
        var client = new NtfyClient(new HttpClient(handler));

        bool sent = await client.SendAsync(Threat, new NtfyOptions("https://ntfy.sh", "my-topic"));

        Assert.True(sent);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://ntfy.sh/my-topic", request.Uri?.ToString());
        Assert.Equal(Threat.Body, request.Body);
        Assert.Equal(Threat.Title, Header(request.Headers, "Title"));
        Assert.Equal("urgent", Header(request.Headers, "Priority"));
        Assert.Equal("rotating_light", Header(request.Headers, "Tags"));
    }

    [Fact]
    public async Task TrailingSlashOnTheServerDoesNotDoubleUp()
    {
        var handler = new StubHandler();
        var client = new NtfyClient(new HttpClient(handler));

        await client.SendAsync(Threat, new NtfyOptions("https://ntfy.example.com/", "topic"));

        Assert.Equal("https://ntfy.example.com/topic", handler.Requests[0].Uri?.ToString());
    }

    [Theory]
    [InlineData(AlertKind.Unreadable, AlertPriority.High, "high", "warning")]
    [InlineData(AlertKind.LocalClear, AlertPriority.Low, "low", "white_check_mark")]
    public async Task PriorityAndTagsFollowTheAlertKind(AlertKind kind, AlertPriority priority, string expectedPriority, string expectedTag)
    {
        var handler = new StubHandler();
        var client = new NtfyClient(new HttpClient(handler));
        var message = new AlertMessage(kind, "title", "body", priority, DateTimeOffset.UtcNow);

        await client.SendAsync(message, new NtfyOptions("https://ntfy.sh", "topic"));

        Assert.Equal(expectedPriority, Header(handler.Requests[0].Headers, "Priority"));
        Assert.Equal(expectedTag, Header(handler.Requests[0].Headers, "Tags"));
    }

    [Fact]
    public async Task RetriesOnceAfterAFailure()
    {
        var handler = new StubHandler
        {
            Responder = attempt => new HttpResponseMessage(attempt == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK),
        };
        var client = new NtfyClient(new HttpClient(handler));

        bool sent = await client.SendAsync(Threat, new NtfyOptions("https://ntfy.sh", "topic"));

        Assert.True(sent);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GivesUpAfterTheRetryWithoutThrowing()
    {
        var handler = new StubHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) };
        var client = new NtfyClient(new HttpClient(handler));

        bool sent = await client.SendAsync(Threat, new NtfyOptions("https://ntfy.sh", "topic"));

        Assert.False(sent);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ANetworkErrorIsReportedAsFailureNotAnException()
    {
        var handler = new ThrowingHandler();
        var client = new NtfyClient(new HttpClient(handler));

        bool sent = await client.SendAsync(Threat, new NtfyOptions("https://ntfy.sh", "topic"));

        Assert.False(sent);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no such host");
    }
}
