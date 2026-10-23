using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechRiders.Api.Models;
using TechRiders.Application;

namespace TechRiders.Api.Controllers;

[ApiController]
[Authorize]
[Route("capabilities")]
[Tags("Application capabilities — REST also works")]
public sealed class OrderCapabilitiesController(GetPendingOrdersHandler pending,
    GetOrderReviewContextHandler review, CancelOrderHandler cancel) : ControllerBase
{
    [HttpGet("pending-orders")]
    public IReadOnlyList<PendingOrderDto> Pending([FromQuery, Required(AllowEmptyStrings = true)] string customerId) =>
        pending.Handle(customerId, User);

    [HttpGet("orders/{id}/review-context")]
    public OrderReviewContextDto Review(string id) => review.Handle(id, User);

    [HttpPost("/orders/{id}/cancellation")]
    [EndpointDescription("Pending duplicate only. Requires signed review token, explicit confirmation, orders.cancel and Idempotency-Key (demo: prefix).")]
    public CancellationResult Cancel(string id, [FromBody, Required] CancellationBody body) =>
        cancel.Handle(new(id, body.ReviewToken, body.Confirmed, Request.Headers["Idempotency-Key"].ToString()), User);
}
