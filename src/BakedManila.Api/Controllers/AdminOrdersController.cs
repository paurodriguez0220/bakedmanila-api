using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public sealed class AdminOrdersController(IOrderRepository orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminOrderDto>>> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var list = await orders.GetFilteredAsync(status, from, to, ct);
        return Ok(list.Select(AdminOrderDto.FromEntity).ToList());
    }
}
