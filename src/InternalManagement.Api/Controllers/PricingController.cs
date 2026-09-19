using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/pricing")]
public class PricingController : BaseApiController
{
    private readonly IPricingService _service;

    public PricingController(IPricingService service)
    {
        _service = service;
    }

    [HttpPost("policies")]
    [HasPermission(Permissions.ChannelFeePolicyManage)]
    public async Task<IActionResult> CreatePolicy([FromBody] CreateChannelFeePolicyRequest request, CancellationToken ct)
    {
        var result = await _service.CreatePolicyAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<ChannelFeePolicyDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo chính sách phí thất bại."));
        return Ok(ApiResponse<ChannelFeePolicyDto>.Ok(result.Value!, "Tạo chính sách phí theo kênh thành công."));
    }

    [HttpPost("simulate")]
    [HasPermission(Permissions.PricingSimulator)]
    public async Task<IActionResult> Simulate([FromBody] PricingSimulationRequest request, CancellationToken ct)
    {
        var result = await _service.SimulateAsync(request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<PricingSimulationDto>.Fail(result.Errors.FirstOrDefault() ?? "Mô phỏng giá thất bại."));
        return Ok(ApiResponse<PricingSimulationDto>.Ok(result.Value!, "Mô phỏng giá và margin thành công."));
    }
}
