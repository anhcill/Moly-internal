using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;

namespace InternalManagement.Application.Features.Fashion.Services;

public interface IPricingService
{
    Task<Result<ChannelFeePolicyDto>> CreatePolicyAsync(CreateChannelFeePolicyRequest request, CancellationToken ct);
    Task<Result<PricingSimulationDto>> SimulateAsync(PricingSimulationRequest request, CancellationToken ct);
}
