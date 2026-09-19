using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Enums;

namespace InternalManagement.IntegrationTests;

public class PayrollWorkflowApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PayrollWorkflowApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var loginReq = new LoginRequest("admin", "Admin@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginData!.Data!.AccessToken;
    }

    private static async Task SeedAttendanceForPayrollAsync(HttpClient client, DateOnly date)
    {
        var employeesResponse = await client.GetAsync("/api/v1/employees?pageSize=100");
        employeesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var employees = await employeesResponse.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<EmployeeDto>>>();

        foreach (var employee in employees!.Data!.Items.Where(e =>
                     e.Status == "Active" && e.EmploymentType == EmploymentType.FULL_TIME))
        {
            var attendance = new CreateAttendanceRecordRequest(employee.Id, date, new TimeOnly(8, 0), new TimeOnly(17, 0), 8, "Present");
            var response = await client.PostAsJsonAsync("/api/v1/attendance", attendance);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task FullPayrollLifecycle_ApiWorkflow_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Create Period
        var uniqueName = "Kỳ Lương Workflow " + Guid.NewGuid().ToString("N")[..6];
        var createReq = new CreatePayrollPeriodRequest(uniqueName, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var createRes = await client.PostAsJsonAsync("/api/v1/payroll/periods", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var periodId = (await createRes.Content.ReadFromJsonAsync<ApiResponse<PayrollPeriodDto>>())!.Data!.Id;

        await SeedAttendanceForPayrollAsync(client, new DateOnly(2026, 6, 1));

        // 2. Calculate
        var calcRes = await client.PostAsync($"/api/v1/payroll/periods/{periodId}/calculate", null);
        calcRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Submit for Review
        var submitRes = await client.PostAsJsonAsync($"/api/v1/payroll/periods/{periodId}/submit-review", new SubmitPayrollForReviewRequest("Kế toán gửi duyệt"));
        submitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitData = await submitRes.Content.ReadFromJsonAsync<ApiResponse<PayrollPeriodDto>>();
        submitData!.Data!.Status.Should().Be(PayrollStatus.Reviewing);

        // 4. Approve
        var approveRes = await client.PostAsJsonAsync($"/api/v1/payroll/periods/{periodId}/approve", new ApprovePayrollRequest("Giám đốc phê duyệt"));
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var approveData = await approveRes.Content.ReadFromJsonAsync<ApiResponse<PayrollPeriodDto>>();
        approveData!.Data!.Status.Should().Be(PayrollStatus.Approved);

        // 5. Mark as Paid
        var paidRes = await client.PostAsJsonAsync($"/api/v1/payroll/periods/{periodId}/mark-paid", new MarkPayrollPaidRequest("Đã chi lương"));
        paidRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var paidData = await paidRes.Content.ReadFromJsonAsync<ApiResponse<PayrollPeriodDto>>();
        paidData!.Data!.Status.Should().Be(PayrollStatus.Paid);

        // 6. Publish
        var publishRes = await client.PostAsJsonAsync($"/api/v1/payroll/periods/{periodId}/publish", new PublishPayrollRequest("Phát hành"));
        publishRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var publishData = await publishRes.Content.ReadFromJsonAsync<ApiResponse<PayrollPeriodDto>>();
        publishData!.Data!.Status.Should().Be(PayrollStatus.Published);

        // 7. Get Approvals History
        var approvalsRes = await client.GetAsync($"/api/v1/payroll/periods/{periodId}/approvals");
        approvalsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var approvalsData = await approvalsRes.Content.ReadFromJsonAsync<ApiResponse<List<PayrollApprovalDto>>>();
        approvalsData!.Data!.Should().HaveCountGreaterOrEqualTo(4);

        // 8. Query My Payslips
        var myPayslipsRes = await client.GetAsync("/api/v1/payroll/my-payslips");
        myPayslipsRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
