using TechRiders.Domain;

namespace TechRiders.Application;

public sealed record OrderDto(string Id, string CustomerId, string Status, decimal Amount, string Priority)
{
    public static OrderDto From(Order o) => new(o.Id, o.CustomerId, o.Status.ToString(), o.Amount, o.Priority.ToString());
}
public sealed record PendingOrderDto(string Id, decimal Amount, string Priority, DateOnly CreatedOn, int ReviewRank, string ReviewHint);
public sealed record ReviewReasonDto(string Code, string Description, string RelatedOrderId, decimal Confidence);
public sealed record OrderReviewContextDto(string OrderId, bool RequiresReview, IReadOnlyList<ReviewReasonDto> Reasons,
    IReadOnlyList<string> AllowedActions, string ReviewToken, int Version);
public sealed record CancelOrderCommand(string OrderId, string ReviewToken, bool Confirmed, string IdempotencyKey);
public sealed record CancellationResult(string OrderId, string PreviousStatus, string CurrentStatus, bool AlreadyApplied);
public sealed record StoredCancellation(string RequestHash, string Actor, CancellationResult Result);
public sealed record ReviewProof(string OrderId, int Version, string RelatedId, int RelatedVersion, string EvidenceHash, long Expires);
public sealed record OrderInput(string CustomerId, string Status, decimal Amount, string Priority, DateOnly CreatedOn,
    string PurchaseOrderReference, string ItemsFingerprint, string DeliveryAddress);
