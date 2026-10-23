namespace TechRiders.Domain;

public enum OrderStatus { Pending, Processing, Shipped, Cancelled }
public enum OrderPriority { Normal, High }

public sealed record Order(
    string Id, string CustomerId, OrderStatus Status, decimal Amount,
    OrderPriority Priority, DateOnly CreatedOn, string PurchaseOrderReference,
    string ItemsFingerprint, string DeliveryAddress, int Version = 1);

public static class OrderRules
{
    public static bool IsDuplicateOf(Order candidate, Order original) =>
        candidate.Id != original.Id &&
        string.CompareOrdinal(candidate.Id, original.Id) > 0 &&
        original.Status != OrderStatus.Cancelled &&
        candidate.CustomerId == original.CustomerId &&
        candidate.Amount == original.Amount &&
        candidate.CreatedOn == original.CreatedOn &&
        !string.IsNullOrWhiteSpace(candidate.PurchaseOrderReference) &&
        candidate.PurchaseOrderReference == original.PurchaseOrderReference &&
        candidate.ItemsFingerprint == original.ItemsFingerprint &&
        candidate.DeliveryAddress == original.DeliveryAddress;

    public static Order? DuplicateOriginal(Order candidate, IEnumerable<Order> orders) =>
        orders.Where(o => IsDuplicateOf(candidate, o)).OrderBy(o => o.Id, StringComparer.Ordinal).FirstOrDefault();

    public static IReadOnlyList<Order> ReviewOrder(IEnumerable<Order> orders)
    {
        var all = orders.ToArray();
        return all.Where(o => o.Status == OrderStatus.Pending)
            .OrderByDescending(o => DuplicateOriginal(o, all) is not null)
            .ThenByDescending(o => o.Priority)
            .ThenBy(o => o.CreatedOn)
            .ThenBy(o => o.Id, StringComparer.Ordinal).ToArray();
    }

    public static Order[] Seed() =>
    [
        new("ORD-1001", "cus-contoso", OrderStatus.Pending, 1250, OrderPriority.Normal,
            new(2026, 9, 15), "PO-001", "keyboard:5", "Madrid"),
        new("ORD-1002", "cus-contoso", OrderStatus.Pending, 8900, OrderPriority.High,
            new(2026, 9, 12), "PO-2026-042", "laptop-pro:5;dock:5", "Madrid HQ"),
        new("ORD-1003", "cus-contoso", OrderStatus.Shipped, 8900, OrderPriority.High,
            new(2026, 9, 12), "PO-2026-043", "monitor:20", "Barcelona"),
        new("ORD-1004", "cus-contoso", OrderStatus.Pending, 8900, OrderPriority.High,
            new(2026, 9, 12), "PO-2026-042", "laptop-pro:5;dock:5", "Madrid HQ")
    ];
}
