using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;

namespace InternalManagement.Api.Controllers;

/// <summary>
/// Endpoint tiếp nhận webhook từ hệ thống bên ngoài.
/// Xác thực qua HMAC SHA-256, không yêu cầu JWT Bearer.
/// </summary>
[Route("api/v1/system/webhooks")]
[AllowAnonymous]
public class WebhooksController : BaseApiController
{
    private readonly IWebhookProcessor _webhookProcessor;

    public WebhooksController(IWebhookProcessor webhookProcessor)
    {
        _webhookProcessor = webhookProcessor;
    }

    /// <summary>
    /// Nhận webhook event từ hệ thống nguồn.
    /// Header X-Hub-Signature-256 chứa chữ ký HMAC.
    /// Trả về 202 Accepted (event mới) hoặc 200 OK (duplicate, idempotent).
    /// </summary>
    [HttpPost("{sourceSystem}")]
    [ProducesResponseType(typeof(ApiResponse<WebhookIngestResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<WebhookIngestResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<WebhookIngestResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestWebhook(
        string sourceSystem,
        [FromBody] WebhookIngestRequest request,
        CancellationToken ct)
    {
        var signature = HttpContext.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        var (accepted, message) = await _webhookProcessor.IngestAsync(
            sourceSystem,
            request.EventId,
            request.EventType,
            request.PayloadJson,
            signature,
            ct);

        var response = new WebhookIngestResponse(accepted, message);

        if (!accepted)
        {
            return BadRequest(ApiResponse<WebhookIngestResponse>.Fail(message));
        }

        // Nếu event đã tồn tại (idempotent skip) → 200 OK
        // Nếu event mới → 202 Accepted
        if (message.Contains("idempotent", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("đã được nhận", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(ApiResponse<WebhookIngestResponse>.Ok(response, message));
        }

        return StatusCode(StatusCodes.Status202Accepted,
            ApiResponse<WebhookIngestResponse>.Ok(response, message));
    }
}
