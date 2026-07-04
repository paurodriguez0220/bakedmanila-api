using BakedManila.Api.Dtos;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderService orderService, IOrderRepository orders)
    : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("orders")]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var order = await orderService.PlaceOrderAsync(request.ToCommand(), ct);
        var dto = OrderDto.FromEntity(order);
        return CreatedAtAction(nameof(Lookup), new { orderNumber = dto.OrderNumber }, dto);
    }

    [HttpGet("{orderNumber}")]
    public async Task<ActionResult<OrderDto>> Lookup(
        string orderNumber, [FromQuery] string phone, CancellationToken ct)
    {
        var order = await orders.GetByNumberAndPhoneAsync(orderNumber, phone, ct);
        return order is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
            : Ok(OrderDto.FromEntity(order));
    }
}
