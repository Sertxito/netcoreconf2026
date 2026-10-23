using TechRiders.Domain;

namespace TechRiders.Tests;

public class DomainTests
{
    [Fact]
    public void ReviewPriorityIsStableAndDuplicateFirst()
    {
        Assert.Equal(["ORD-1004", "ORD-1002", "ORD-1001"], OrderRules.ReviewOrder(OrderRules.Seed().Reverse()).Select(o => o.Id));
    }

    [Fact]
    public void EqualAmountAndDateAreNotDuplicateEvidence()
    {
        var orders = OrderRules.Seed();
        Assert.False(OrderRules.IsDuplicateOf(orders[2], orders[1]));
        Assert.True(OrderRules.IsDuplicateOf(orders[3], orders[1]));
        Assert.False(OrderRules.IsDuplicateOf(orders[1], orders[3]));
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("items")]
    [InlineData("delivery")]
    [InlineData("customer")]
    public void AnyMissingEvidencePreventsDuplicateConclusion(string changed)
    {
        var original = OrderRules.Seed()[1];
        var candidate = OrderRules.Seed()[3];
        candidate = changed switch
        {
            "reference" => candidate with { PurchaseOrderReference = "different" },
            "items" => candidate with { ItemsFingerprint = "different" },
            "delivery" => candidate with { DeliveryAddress = "different" },
            _ => candidate with { CustomerId = "different" }
        };
        Assert.False(OrderRules.IsDuplicateOf(candidate, original));
    }

    [Fact]
    public void PriorityDateThenIdBreakTies()
    {
        var order = OrderRules.Seed()[0];
        var orders = new[] { order with { Id = "C", PurchaseOrderReference = "C" },
            order with { Id = "B", PurchaseOrderReference = "B" }, order with { Id = "A", PurchaseOrderReference = "A" } };
        Assert.Equal(["A", "B", "C"], OrderRules.ReviewOrder(orders).Select(o => o.Id));
    }
}
