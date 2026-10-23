using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol.Server;
using TechRiders.Application;

namespace TechRiders.Mcp;

[McpServerToolType]
public sealed class OrderTools(GetPendingOrdersHandler pending, GetOrderReviewContextHandler review,
    CancelOrderHandler cancel, IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private ClaimsPrincipal Actor => http.HttpContext?.User ?? new ClaimsPrincipal();
    private string Execute<T>(string operation, Func<T> action)
    {
        using var span = Telemetry.Source.StartActivity($"tool.{operation}");
        try
        {
            return JsonSerializer.Serialize(action(), Json);
        }
        catch (BusinessException ex)
        {
            return JsonSerializer.Serialize(new { error = new { code = ex.Code, detail = ex.Message, status = ex.Status,
                correlationId = System.Diagnostics.Activity.Current?.TraceId.ToString() } }, Json);
        }
    }

    [McpServerTool(Name = "get_pending_orders", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Gets Contoso pending orders (customerId=cus-contoso), sorted by duplicate evidence first, then priority, date and stable ID. Read-only. Use review context before proposing cancellation.")]
    public string GetPendingOrders([Description("Stable customer ID, exactly cus-contoso (not display name Contoso).")] string customerId) =>
        Execute("get_pending_orders", () => pending.Handle(customerId, Actor));

    [McpServerTool(Name = "get_order_review_context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Gets server-verified duplicate evidence, related order, allowed actions and signed 15-minute version-bound reviewToken. Read-only. Show the exact order and evidence to a human before requesting confirmation.")]
    public string GetOrderReviewContext([Description("Exact order ID selected from pending review ranks.")] string orderId) =>
        Execute("get_order_review_context", () => review.Handle(orderId, Actor));

    [McpServerTool(Name = "cancel_order", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false), Description(
        "WRITES: cancels exactly one Pending duplicate after explicit human confirmation. Requires orders.cancel in server-validated identity, unchanged signed reviewToken and demo: idempotencyKey. Retry identical arguments with the same key; changed payload conflicts. Shipped orders cannot be cancelled.")]
    public string CancelOrder(
        [Description("Exact human-reviewed order ID.")] string orderId,
        [Description("Unmodified token returned by get_order_review_context for this order.")] string reviewToken,
        [Description("True only after the human explicitly confirmed this order; default false.")] bool confirmed = false,
        [Description("Unique demo: prefixed key. Reuse ONLY for identical retry.")] string idempotencyKey = "") =>
        Execute("cancel_order", () => cancel.Handle(new(orderId, reviewToken, confirmed, idempotencyKey), Actor));
}
