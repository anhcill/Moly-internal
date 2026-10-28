using System.Net;
using System.Text;
using FluentAssertions;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Infrastructure.Integration.Connectors;
using Microsoft.Extensions.Configuration;

namespace InternalManagement.UnitTests.Integration;

public sealed class CscaCourseLmsPayrollClientTests
{
    [Fact]
    public async Task QueryTeacherCostsAsync_ShouldUseSignedPayrollEndpointAndParseItems()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            var json = """
                {
                  "success": true,
                  "correlationId": "corr-1",
                  "data": {
                    "sourceSystem": "CSCA_COURSE",
                    "calculatedAt": "2026-10-27T01:00:00Z",
                    "includeProjected": true,
                    "items": [{
                      "teacherSourceId": "4f53f5d6632f4dabb649319f66ef7934",
                      "teacherLmsId": 7,
                      "teacherName": "Teacher A",
                      "teacherUsername": "teacher-a",
                      "courseSourceId": "course-1",
                      "courseTitle": "CSCA",
                      "classSourceId": "class-1",
                      "classLmsId": 8,
                      "classTitle": "Class A",
                      "teachingRole": "lead",
                      "sessionSourceId": "session-1",
                      "sessionLmsId": 9,
                      "sessionTitle": "Lesson 1",
                      "startTime": "2026-10-01T01:00:00Z",
                      "endTime": "2026-10-01T03:00:00Z",
                      "status": "ended",
                      "isCompleted": true,
                      "durationHours": 2,
                      "effectiveHourlyRate": 200000,
                      "accruedAmount": 400000,
                      "projectedAmount": 400000,
                      "bonusAmount": 50000,
                      "deductionAmount": 0,
                      "paymentStatus": "pending",
                      "paidAt": null,
                      "sourceUpdatedAt": "2026-10-02T01:00:00Z"
                    }]
                  }
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Integrations:CscaCourseLms:BaseUrl"] = "https://csca.example",
            ["Integrations:CscaCourseLms:ServiceToken"] = "service-token",
            ["Integrations:CscaCourseLms:IntegrationKey"] = "integration-key",
            ["Integrations:CscaCourseLms:HmacSecret"] = "a-secret-long-enough-for-test"
        }).Build();
        var client = new CscaCourseLmsClient(new HttpClient(handler), configuration);

        var result = await client.QueryTeacherCostsAsync(
            new LmsTeacherCostQuery(IncludeProjected: true),
            new LmsOutboundRequestContext("idem-1", "corr-1"),
            CancellationToken.None);

        result.SourceSystem.Should().Be("CSCA_COURSE");
        result.Items.Should().ContainSingle();
        result.Items[0].AccruedAmount.Should().Be(400000);
        captured.Should().NotBeNull();
        captured!.RequestUri!.AbsolutePath.Should().Be("/api/integrations/v1/payroll/teacher-costs/query");
        captured.Headers.Should().ContainKey("X-Signature");
        captured.Headers.Should().ContainKey("X-Integration-Key");
        captured.Headers.GetValues("Idempotency-Key").Should().ContainSingle("idem-1");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
