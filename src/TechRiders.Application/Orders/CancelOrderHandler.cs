using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using TechRiders.Domain;

namespace TechRiders.Application;

public sealed class CancelOrderHandler(IOrderStore store, IReviewSigner signer)
{
    public CancellationResult Handle(CancelOrderCommand command, ClaimsPrincipal actor)
    {
        using var span = Telemetry.Source.StartActivity("CancelOrder");
        span?.SetTag("order.id", command.OrderId);
        var actorId = Access.Demand(actor, "orders.cancel");
        if (!command.Confirmed)
            throw new BusinessException("CONFIRMATION_REQUIRED", "Explicit human confirmation for this reviewed order is required.", 409);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || !command.IdempotencyKey.StartsWith("demo:", StringComparison.Ordinal) ||
            command.IdempotencyKey.Length > 128)
            throw new BusinessException("INVALID_IDEMPOTENCY_KEY", "Use a demo: prefixed key, at most 128 characters.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));
        return store.InTransaction(session =>
        {
            using (var idempotency = Telemetry.Source.StartActivity("idempotency"))
            {
                var previous = session.FindCancellation(command.IdempotencyKey);
                if (previous is not null)
                {
                    if (previous.RequestHash != hash || previous.Actor != actorId)
                        throw new BusinessException("IDEMPOTENCY_CONFLICT", "This key belongs to a different request or identity.", 409);
                    return previous.Result with { AlreadyApplied = true };
                }
            }
            var order = session.Find(command.OrderId) ?? throw new BusinessException("ORDER_NOT_FOUND", "Unknown order.", 404);
            if (order.Status != OrderStatus.Pending)
                throw new BusinessException("INVALID_ORDER_STATE", "Only Pending orders may be cancelled.", 409);
            var proof = signer.Verify(command.ReviewToken);
            var original = session.Find(proof.RelatedId);
            if (proof.OrderId != order.Id || proof.Version != order.Version ||
                proof.Expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds() || original is null ||
                proof.RelatedVersion != original.Version || !OrderRules.IsDuplicateOf(order, original) ||
                proof.EvidenceHash != ReviewEvidence.Hash(order, original))
                throw new BusinessException("STALE_REVIEW_CONTEXT", "Review context is stale or does not prove this duplicate. Review again.", 409);
            session.Save(order with { Status = OrderStatus.Cancelled, Version = order.Version + 1 });
            var result = new CancellationResult(order.Id, "Pending", "Cancelled", false);
            session.SaveCancellation(command.IdempotencyKey, new(hash, actorId, result));
            span?.SetTag("idempotency.key", command.IdempotencyKey);
            return result;
        });
    }
}
