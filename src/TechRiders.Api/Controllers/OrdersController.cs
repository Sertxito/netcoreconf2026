using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechRiders.Application;

namespace TechRiders.Api.Controllers;

[ApiController]
[Authorize]
[Route("orders")]
[Tags("Orders — traditional CRUD")]
public sealed class OrdersController(OrderCrudHandler handler) : ControllerBase
{
    [HttpGet(Name = "ListOrders")]
    [EndpointDescription("Optional customerId: cus-contoso. Status: Pending, Processing, Shipped or Cancelled (case sensitive).")]
    [ProducesResponseType<IReadOnlyList<OrderDto>>(200)]
    [ProducesResponseType<ProblemDetails>(400, "application/problem+json")]
    public IReadOnlyList<OrderDto> List([FromQuery] string? customerId, [FromQuery] string? status) =>
        handler.List(customerId, status, User);

    [HttpGet("{id}", Name = "GetOrder")]
    public OrderDto Get(string id) => handler.Get(id, User);

    [HttpPost(Name = "CreateOrder")]
    public IActionResult Create([FromBody, Required] OrderInput input)
    {
        var id = $"ORD-{Guid.NewGuid():N}";
        return Created($"/orders/{id}", handler.Save(id, input, true, User));
    }

    [HttpPut("{id}", Name = "UpdateOrder")]
    public OrderDto Update(string id, [FromBody, Required] OrderInput input) =>
        handler.Save(id, input, false, User);

    [HttpDelete("{id}", Name = "DeleteOrder")]
    public IActionResult Delete(string id)
    {
        handler.Delete(id, User);
        return NoContent();
    }
}
