using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;
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

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<AdminOrderDto>> UpdateStatus(
        int id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(id, ct)
            ?? throw new OrderNotFoundException(id);
        order.TransitionTo(request.Status!.Value); // validated: throws InvalidStatusTransitionException → 409
        await orders.SaveChangesAsync(ct);
        return Ok(AdminOrderDto.FromEntity(order));
    }

    [HttpPatch("{id:int}/payment")]
    public async Task<ActionResult<AdminOrderDto>> UpdatePayment(
        int id, UpdatePaymentStatusRequest request, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(id, ct)
            ?? throw new OrderNotFoundException(id);
        order.MarkPayment(request.PaymentStatus!.Value);
        await orders.SaveChangesAsync(ct);
        return Ok(AdminOrderDto.FromEntity(order));
    }
}
