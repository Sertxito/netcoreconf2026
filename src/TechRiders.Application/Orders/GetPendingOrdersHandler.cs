using System.Security.Claims;
using TechRiders.Domain;

namespace TechRiders.Application;

public sealed class GetPendingOrdersHandler(IOrderStore store)
{
    public IReadOnlyList<PendingOrderDto> Handle(string customerId, ClaimsPrincipal actor)
    {
        using var span = Telemetry.Source.StartActivity("GetPendingOrders");
        Access.Demand(actor, "orders.read");
        Contracts.Customer(customerId);
        return store.InTransaction<IReadOnlyList<PendingOrderDto>>(session =>
        {
            var all = session.All().Where(order => order.CustomerId == customerId).ToArray();
            return OrderRules.ReviewOrder(all)
                .Select((order, index) => new PendingOrderDto(order.Id, order.Amount, order.Priority.ToString(),
                    order.CreatedOn, index + 1, OrderRules.DuplicateOriginal(order, all) is not null
                        ? "Possible duplicate; request review context."
                        : "Priority, then oldest date, then ID."))
                .ToArray();
        });
    }
}
