using System.Security.Claims;

namespace TechRiders.Application;

public static class Access
{
    public static string Demand(ClaimsPrincipal actor, string permission)
    {
        using var span = Telemetry.Source.StartActivity("authorization");
        if (actor.Identity?.IsAuthenticated != true)
            throw new BusinessException("UNAUTHENTICATED", "Demo bearer identity required.", 401);
        if (!actor.HasClaim("permission", permission))
            throw new BusinessException("ORDER_CANCELLATION_NOT_ALLOWED", $"Required permission: {permission}.", 403);
        var id = actor.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(id))
            throw new BusinessException("UNAUTHENTICATED", "Identity has no subject.", 401);
        span?.SetTag("user.id", id);
        return id;
    }
}
