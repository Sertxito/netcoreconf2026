using System.Security.Claims;
using TechRiders.Domain;

namespace TechRiders.Application;

public sealed class GetOrderReviewContextHandler(IOrderStore store, IReviewSigner signer)
{
    public OrderReviewContextDto Handle(string orderId, ClaimsPrincipal actor)
    {
        using var span = Telemetry.Source.StartActivity("GetOrderReviewContext");
        Access.Demand(actor, "orders.read");
        return store.InTransaction(session =>
        {
            var order = session.Find(orderId) ?? throw new BusinessException("ORDER_NOT_FOUND", "Unknown order.", 404);
            var original = OrderRules.DuplicateOriginal(order, session.All());
            if (original is null || order.Status != OrderStatus.Pending)
                return new OrderReviewContextDto(order.Id, false, [], ["Review"], "", order.Version);

            ReviewReasonDto[] reasons = [new("POSSIBLE_DUPLICATE",
                $"Same customer, amount, date, purchase reference ({order.PurchaseOrderReference}), line items ({order.ItemsFingerprint}) and delivery address ({order.DeliveryAddress}).",
                original.Id, 0.98m)];
            var token = signer.Sign(new(order.Id, order.Version, original.Id, original.Version,
                EvidenceHash(order, original), DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()));
            return new OrderReviewContextDto(order.Id, true, reasons, ["Review", "Cancel"], token, order.Version);
        });
    }

    public static string EvidenceHash(Order order, Order original) => ReviewEvidence.Hash(order, original);
}
