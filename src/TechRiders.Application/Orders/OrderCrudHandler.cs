using System.Security.Claims;
using TechRiders.Domain;

namespace TechRiders.Application;

public sealed class OrderCrudHandler(IOrderStore store)
{
    public IReadOnlyList<OrderDto> List(string? customerId, string? status, ClaimsPrincipal actor)
    {
        using var span = Telemetry.Source.StartActivity("ListOrders");
        Access.Demand(actor, "orders.read");
        var parsed = status is null ? (OrderStatus?)null : Contracts.Status(status);
        if (customerId is not null) Contracts.Customer(customerId);
        return store.InTransaction<IReadOnlyList<OrderDto>>(session => session.All()
            .Where(order => (customerId is null || order.CustomerId == customerId) && (parsed is null || order.Status == parsed))
            .OrderBy(order => order.Id, StringComparer.Ordinal).Select(OrderDto.From).ToArray());
    }

    public OrderDto Get(string id, ClaimsPrincipal actor)
    {
        Access.Demand(actor, "orders.read");
        return store.InTransaction(session =>
            OrderDto.From(session.Find(id) ?? throw new BusinessException("ORDER_NOT_FOUND", "Unknown order.", 404)));
    }

    public OrderDto Save(string id, OrderInput input, bool create, ClaimsPrincipal actor)
    {
        Access.Demand(actor, "orders.write");
        Contracts.Customer(input.CustomerId);
        var status = Contracts.Status(input.Status);
        var priority = Contracts.PriorityAndEvidence(input);
        if (status != OrderStatus.Pending)
            throw new BusinessException("INVALID_ORDER_STATE", "CRUD only creates/edits Pending orders. Use the guarded cancellation capability.", 409);
        return store.InTransaction(session =>
        {
            var previous = session.Find(id);
            if (create && previous is not null) throw new BusinessException("ORDER_EXISTS", "Order already exists.", 409);
            if (!create && previous is null) throw new BusinessException("ORDER_NOT_FOUND", "Unknown order.", 404);
            if (previous is not null && previous.Status != OrderStatus.Pending)
                throw new BusinessException("INVALID_ORDER_STATE", "Only Pending orders can be edited.", 409);
            var order = new Order(id, input.CustomerId, status, input.Amount, priority, input.CreatedOn,
                input.PurchaseOrderReference, input.ItemsFingerprint, input.DeliveryAddress, (previous?.Version ?? 0) + 1);
            session.Save(order);
            return OrderDto.From(order);
        });
    }

    public void Delete(string id, ClaimsPrincipal actor)
    {
        Access.Demand(actor, "orders.write");
        if (OrderRules.Seed().Any(order => order.Id == id))
            throw new BusinessException("PROTECTED_DEMO_ORDER", "Seed orders cannot be deleted; use the cancellation capability or explicit local reset.", 409);
        store.InTransaction(session =>
        {
            var order = session.Find(id) ?? throw new BusinessException("ORDER_NOT_FOUND", "Unknown order.", 404);
            if (order.Status != OrderStatus.Pending)
                throw new BusinessException("INVALID_ORDER_STATE", "Only Pending orders can be deleted.", 409);
            session.Delete(id);
            return true;
        });
    }
}
