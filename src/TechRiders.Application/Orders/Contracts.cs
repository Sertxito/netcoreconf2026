using TechRiders.Domain;

namespace TechRiders.Application;

public static class Contracts
{
    public static void Customer(string? id)
    {
        if (id != "cus-contoso")
            throw new BusinessException("INVALID_CUSTOMER_ID", "Use the stable customer identifier cus-contoso, not the display name Contoso.");
    }

    public static OrderStatus Status(string? status)
    {
        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: false, out var parsed) ||
            !Enum.IsDefined(parsed) || parsed.ToString() != status)
            throw new BusinessException("INVALID_ORDER_STATUS", $"The value '{status}' is invalid. Allowed values are Pending, Processing, Shipped and Cancelled.");
        return parsed;
    }

    internal static OrderPriority PriorityAndEvidence(OrderInput input)
    {
        if (!Enum.TryParse<OrderPriority>(input.Priority, false, out var priority) ||
            !Enum.IsDefined(priority) || priority.ToString() != input.Priority ||
            input.Amount <= 0 || string.IsNullOrWhiteSpace(input.PurchaseOrderReference) ||
            string.IsNullOrWhiteSpace(input.ItemsFingerprint) || string.IsNullOrWhiteSpace(input.DeliveryAddress) ||
            input.CreatedOn == default)
            throw new BusinessException("INVALID_ORDER", "Positive amount, exact priority Normal/High, date and complete evidence are required.");
        return priority;
    }
}
