using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Infrastructure.Integration.Connectors;

namespace InternalManagement.UnitTests.Integration;

public class RailwayConnectorTests
{
    [Fact]
    public async Task InterviewConnector_ShouldPageAndNormalizeAdminUsers()
    {
        var handler = new StubHandler();

        handler.Responses = [
            JsonResponse("""
                {"users":[{"id":"u-1","full_name":"Nguyen A","email":"a@example.com","created_at":"2026-08-01T00:00:00Z","_count":{"interviewSessions":2}},{"id":"u-2","full_name":"Nguyen B","email":"b@example.com"}],"pagination":{"currentPage":1,"totalPages":2,"totalUsers":3}}
                """),
            JsonResponse("""
                {"users":[{"id":"u-3","full_name":"Nguyen C","email":"c@example.com"}],"pagination":{"currentPage":2,"totalPages":2,"totalUsers":3}}
                """)
        ];

        var connector = CreateInterviewConnector(handler, pageSize: 2);

        var first = await connector.PullAsync("InterviewCustomers", new(), CancellationToken.None);
        var second = await connector.PullAsync("InterviewCustomers", first.NextCursor, CancellationToken.None);

        first.Items.Should().HaveCount(2);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Offset.Should().Be(2);
        first.Items[0].GetProperty("sessionCount").GetInt32().Should().Be(2);
        second.Items.Should().ContainSingle();
        second.HasMore.Should().BeFalse();
        second.NextCursor.Offset.Should().Be(3);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.Query.Should().Contain("page=1");
        handler.Requests[1].RequestUri!.Query.Should().Contain("page=2");
    }

    [Fact]
    public async Task RailwayConnector_ShouldRetryRateLimitAndSendBearerToken()
    {
        var handler = new StubHandler
        {
            Responses = [
                new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                JsonResponse("""{"users":[],"pagination":{"currentPage":1,"totalPages":1,"totalUsers":0}}""")
            ]
        };

        var connector = CreateInterviewConnector(handler, pageSize: 10, bearerToken: "secret-token");
        var page = await connector.PullAsync("InterviewCustomers", new(), CancellationToken.None);

        page.Items.Should().BeEmpty();
        handler.Requests.Should().HaveCount(2);
        handler.Requests.All(request => request.Headers.Authorization?.Scheme == "Bearer"
                                        && request.Headers.Authorization.Parameter == "secret-token")
            .Should().BeTrue();
    }

    [Fact]
    public async Task CscaMoliStudioConnector_ShouldFailPaymentsWithoutReadOnlyEndpoint()
    {
        var connector = new CscaMoliStudioConnector(
            new HttpClient(new StubHandler()),
            CreateConfiguration("CscaMoliStudio", pageSize: 10),
            NullLogger<CscaMoliStudioConnector>.Instance);

        var action = () => connector.PullAsync("Payments", new(), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PaymentsPath*");
    }

    private static CscaInterviewConnector CreateInterviewConnector(
        HttpMessageHandler handler,
        int pageSize,
        string? bearerToken = null)
    {
        return new CscaInterviewConnector(
            new HttpClient(handler),
            CreateConfiguration("CscaInterview", pageSize, bearerToken),
            NullLogger<CscaInterviewConnector>.Instance);
    }

    private static IConfiguration CreateConfiguration(string key, int pageSize, string? bearerToken = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Integrations:{key}:BaseUrl"] = "https://test.invalid",
                [$"Integrations:{key}:PageSize"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [$"Integrations:{key}:TimeoutSeconds"] = "5",
                [$"Integrations:{key}:BearerToken"] = bearerToken
            })
            .Build();

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private int _responseIndex;
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage>? fallback = null)
        {
            _fallback = fallback;
        }

        public List<HttpRequestMessage> Requests => _requests;
        public List<HttpResponseMessage> Responses { get; set; } = [];
        private readonly List<HttpRequestMessage> _requests = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (_responseIndex < Responses.Count)
                return Task.FromResult(Responses[_responseIndex++]);

            return Task.FromResult(_fallback?.Invoke(request) ?? JsonResponse("{}"));
        }
    }
}
