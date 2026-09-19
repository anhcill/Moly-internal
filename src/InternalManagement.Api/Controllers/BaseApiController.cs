using Microsoft.AspNetCore.Mvc;

namespace InternalManagement.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
}
