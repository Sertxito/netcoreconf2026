using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TechRiders.AgentDemo;
using TechRiders.Application;
using TechRiders.Domain;

namespace TechRiders.Tests;

public sealed class IntegrationTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture fixture;
    public IntegrationTests(ServerFixture fixture)
    {
        this.fixture = fixture;
        fixture.Store.ResetDemoData();
    }

    private int Version(string id = "ORD-1004") => fixture.Store.InTransaction(s => s.Find(id)!.Version);
    private OrderStatus Status(string id = "ORD-1004") => fixture.Store.InTransaction(s => s.Find(id)!.Status);
    private static Dictionary<string, object?> Cancellation(ReviewRun review, bool confirmed = true, string? key = null) =>
        new() { ["orderId"] = review.Candidate.Id, ["reviewToken"] = review.Context.ReviewToken,
            ["confirmed"] = confirmed, ["idempotencyKey"] = key ?? $"demo:{review.RunId}" };

    [Fact]
    public async Task OpenApiAndTeachingFailureUseRealHttpAndNeverWrite()
    {
        var version = Version();
        var result = JsonSerializer.SerializeToElement(await fixture.Runner.RestAsync(), DemoRunner.Json);
        Assert.Equal(400, result.GetProperty("status").GetInt32());
        Assert.Contains("GET /orders", result.GetProperty("operations").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(3, result.GetProperty("correctedRead").GetArrayLength());
        Assert.Equal(version, Version());
        Assert.Equal(OrderStatus.Pending, Status());
    }

    [Theory]
    [InlineData("Contoso", "Pending", "INVALID_CUSTOMER_ID")]
    [InlineData("cus-contoso", "pending", "INVALID_ORDER_STATUS")]
    [InlineData("cus-contoso", "1", "INVALID_ORDER_STATUS")]
    public async Task InvalidContractsReturnActionableProblemDetails(string customer, string status, string code)
    {
        using var http = fixture.Runner.CreateHttpClient();
        using var result = await http.GetAsync($"/orders?customerId={customer}&status={status}");
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("application/problem+json", result.Content.Headers.ContentType?.MediaType);
        var problem = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task OpenApiPreservesRoutesOperationNamesSchemasAndJsonContracts()
    {
        using var http = fixture.Runner.CreateHttpClient();
        var document = await http.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = document.GetProperty("paths");
        Assert.Equal(new[]
        {
            "/capabilities/orders/{id}/review-context", "/capabilities/pending-orders",
            "/health", "/orders", "/orders/{id}", "/orders/{id}/cancellation"
        }, paths.EnumerateObject().Select(path => path.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["get", "post"], paths.GetProperty("/orders").EnumerateObject().Select(operation => operation.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["delete", "get", "put"], paths.GetProperty("/orders/{id}").EnumerateObject().Select(operation => operation.Name).Order(StringComparer.Ordinal));

        foreach (var (path, method, name) in new[]
        {
            ("/orders", "get", "ListOrders"), ("/orders", "post", "CreateOrder"),
            ("/orders/{id}", "get", "GetOrder"), ("/orders/{id}", "put", "UpdateOrder"),
            ("/orders/{id}", "delete", "DeleteOrder")
        })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            Assert.Equal(name, operation.GetProperty("operationId").GetString());
            Assert.Equal("Orders — traditional CRUD", operation.GetProperty("tags")[0].GetString());
        }

        foreach (var path in paths.EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        {
            if (operation.Value.TryGetProperty("requestBody", out var body))
            {
                Assert.True(body.GetProperty("required").GetBoolean());
                Assert.Equal(["application/json"], body.GetProperty("content").EnumerateObject().Select(content => content.Name));
            }
            foreach (var response in operation.Value.GetProperty("responses").EnumerateObject())
                if (response.Value.TryGetProperty("content", out var content))
                    Assert.Equal([response.Name == "400" ? "application/problem+json" : "application/json"],
                        content.EnumerateObject().Select(media => media.Name));
        }

        var list = paths.GetProperty("/orders").GetProperty("get");
        Assert.True(list.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(list.GetProperty("responses").TryGetProperty("400", out _));
        Assert.All(list.GetProperty("parameters").EnumerateArray(), parameter => Assert.False(parameter.TryGetProperty("required", out _)));
        Assert.True(paths.GetProperty("/capabilities/pending-orders").GetProperty("get").GetProperty("parameters")[0].GetProperty("required").GetBoolean());
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["amount", "customerId", "id", "priority", "status"],
            schemas.GetProperty("OrderDto").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["confirmed", "reviewToken"],
            schemas.GetProperty("CancellationBody").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(document.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        Assert.True(document.GetProperty("security")[0].TryGetProperty("Bearer", out _));
    }

    [Theory]
    [InlineData("/orders?status=", "INVALID_ORDER_STATUS")]
    [InlineData("/orders?status=%20", "INVALID_ORDER_STATUS")]
    [InlineData("/orders?customerId=", "INVALID_CUSTOMER_ID")]
    [InlineData("/orders?customerId=%20", "INVALID_CUSTOMER_ID")]
    [InlineData("/orders?status=Pending&status=Shipped", "INVALID_ORDER_STATUS")]
    [InlineData("/orders?customerId=cus-contoso&customerId=Contoso", "INVALID_CUSTOMER_ID")]
    [InlineData("/orders?customerId=Contoso&status=pending", "INVALID_ORDER_STATUS")]
    [InlineData("/capabilities/pending-orders?customerId=", "INVALID_CUSTOMER_ID")]
    public async Task QueryBindingPreservesEmptyValuesAndBusinessValidationOrder(string path, string code)
    {
        using var http = fixture.Runner.CreateHttpClient();
        var before = Version();
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(before, Version());
    }

    [Theory]
    [InlineData("{}", false, "INVALID_CUSTOMER_ID", 400)]
    [InlineData("{\"customerId\":\"cus-contoso\"}", false, "INVALID_ORDER_STATUS", 400)]
    [InlineData("{\"customerId\":\"cus-contoso\",\"status\":\"Pending\"}", false, "INVALID_ORDER", 400)]
    [InlineData("{}", true, "ORDER_CANCELLATION_NOT_ALLOWED", 403)]
    public async Task JsonBindingLeavesSemanticValidationAndPermissionsToSharedHandlers(string body, bool readOnly, string code, int status)
    {
        using var http = fixture.Runner.CreateHttpClient(canCancel: !readOnly);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/orders", content);
        Assert.Equal(status, (int)response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{}", null, "CONFIRMATION_REQUIRED", 409)]
    [InlineData("{\"confirmed\":true}", null, "INVALID_IDEMPOTENCY_KEY", 400)]
    [InlineData("{\"confirmed\":true}", "demo:missing-token", "INVALID_SIGNED_CONTEXT", 409)]
    public async Task CancellationControllerPreservesConfirmationKeyAndProofValidationOrder(string body, string? key, string code, int status)
    {
        using var http = fixture.Runner.CreateHttpClient();
        if (key is not null) http.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var before = Version();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/orders/ORD-1004/cancellation", content);
        Assert.Equal(status, (int)response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(before, Version());
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task InvalidJsonOrMissingBodyIsRejectedWithoutWriting(string body)
    {
        using var http = fixture.Runner.CreateHttpClient();
        var before = Version();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/orders/ORD-1004/cancellation", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, Version());
    }

    [Fact]
    public async Task MissingRequiredQueryParameterDoesNotExecuteCapability()
    {
        using var http = fixture.Runner.CreateHttpClient();
        var before = Version();
        using var response = await http.GetAsync("/capabilities/pending-orders");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, Version());
    }

    [Fact]
    public async Task ControllerEndpointsKeepAuthenticationAndHttpMethods()
    {
        using var anonymous = new HttpClient { BaseAddress = new(fixture.ApiUrl) };
        foreach (var path in new[] { "/orders", "/orders/ORD-1004", "/capabilities/pending-orders?customerId=cus-contoso",
            "/capabilities/orders/ORD-1004/review-context" })
        {
            using var denied = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        using var malformed = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
        using var deniedWrite = await anonymous.PostAsync("/orders/ORD-1004/cancellation", malformed);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedWrite.StatusCode);
        using var http = fixture.Runner.CreateHttpClient();
        using var unsupported = await http.PatchAsync("/orders/ORD-1004", null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupported.StatusCode);
        using var withSlash = await http.GetAsync("/orders/");
        Assert.Equal(HttpStatusCode.OK, withSlash.StatusCode);
        var pending = await http.GetFromJsonAsync<PendingOrderDto[]>("/capabilities/pending-orders?customerId=cus-contoso");
        Assert.Equal("ORD-1004", pending![0].Id);
        using var health = await anonymous.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task OfficialMcpTransportListsExactlyThreeDocumentedToolsAndRealEvidence()
    {
        await using var client = await fixture.Runner.ConnectAsync();
        var tools = await client.ListToolsAsync();
        Assert.Equal(["cancel_order", "get_order_review_context", "get_pending_orders"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
        var review = await fixture.Runner.ReviewAsync();
        Assert.Equal("ORD-1004", review.Candidate.Id);
        Assert.Equal("ORD-1002", Assert.Single(review.Context.Reasons).RelatedOrderId);
        Assert.Contains("PO-2026-042", review.Context.Reasons[0].Description);
        Assert.Contains("laptop-pro:5;dock:5", review.Context.Reasons[0].Description);
        Assert.Contains("Cancel", review.Context.AllowedActions);
    }

    [Fact]
    public async Task ConfirmedMcpWriteIsVisibleThroughRestAndRetryMutatesOnlyOnce()
    {
        var before = Version();
        var review = await fixture.Runner.ReviewAsync();
        var results = await fixture.Runner.ConfirmAsync(review, true);
        Assert.False(results[0].AlreadyApplied);
        Assert.True(results[1].AlreadyApplied);
        Assert.Equal(results[0] with { AlreadyApplied = true }, results[1]);
        Assert.Equal(before + 1, Version());
        using var http = fixture.Runner.CreateHttpClient();
        var order = await http.GetFromJsonAsync<OrderDto>("/orders/ORD-1004");
        Assert.Equal("Cancelled", order!.Status);
        Assert.Equal(OrderStatus.Pending, Status("ORD-1002"));
    }

    [Fact]
    public async Task SameIdempotencyKeyWithChangedPayloadConflictsAndDoesNotWrite()
    {
        var review = await fixture.Runner.ReviewAsync();
        await using var client = await fixture.Runner.ConnectAsync();
        var args = Cancellation(review);
        await DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", args);
        var version = Version();
        args["orderId"] = "ORD-1002";
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", args));
        Assert.Equal("IDEMPOTENCY_CONFLICT", error.Code);
        Assert.Equal(version, Version());
        Assert.Equal(OrderStatus.Pending, Status("ORD-1002"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrFalseConfirmationDoesNotWrite(bool missing)
    {
        var review = await fixture.Runner.ReviewAsync();
        var before = Version();
        await using var client = await fixture.Runner.ConnectAsync();
        var args = Cancellation(review, false);
        if (missing) args.Remove("confirmed");
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", args));
        Assert.Equal("CONFIRMATION_REQUIRED", error.Code);
        Assert.Equal(before, Version());
        Assert.Equal(OrderStatus.Pending, Status());
    }

    [Fact]
    public async Task AgentDenialDoesNotEvenCallTheWriteTool()
    {
        var review = await fixture.Runner.ReviewAsync();
        var before = Version();
        var error = await Assert.ThrowsAsync<BusinessException>(() => fixture.Runner.ConfirmAsync(review, false));
        Assert.Equal("CONFIRMATION_REQUIRED", error.Code);
        Assert.Equal(before, Version());
    }

    [Fact]
    public async Task ReadOnlySignedIdentityCannotWriteEvenIfClientClaimsScopes()
    {
        var review = await fixture.Runner.ReviewAsync();
        var before = Version();
        using var http = fixture.Runner.CreateHttpClient(canCancel: false);
        http.DefaultRequestHeaders.Add("X-Actor", "admin");
        http.DefaultRequestHeaders.Add("X-Scopes", "orders.cancel orders.write");
        http.DefaultRequestHeaders.Add("Idempotency-Key", $"demo:{review.RunId}");
        using var rest = await http.PostAsJsonAsync("/orders/ORD-1004/cancellation", new { reviewToken = review.Context.ReviewToken, confirmed = true });
        Assert.Equal(HttpStatusCode.Forbidden, rest.StatusCode);
        await using var client = await fixture.Runner.ConnectAsync(canCancel: false);
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", Cancellation(review)));
        Assert.Equal("ORDER_CANCELLATION_NOT_ALLOWED", error.Code);
        Assert.Equal(before, Version());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("admin.orders.cancel")]
    public async Task AnonymousOrForgedIdentityCannotWrite(string? token)
    {
        using var http = new HttpClient { BaseAddress = new(fixture.ApiUrl) };
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Actor", "admin");
        http.DefaultRequestHeaders.Add("X-Scopes", "orders.cancel");
        var before = Version();
        using var result = await http.PostAsJsonAsync("/orders/ORD-1004/cancellation", new { reviewToken = "fake", confirmed = true });
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(before, Version());
    }

    [Fact]
    public async Task ShippedOrderCannotBeCancelled()
    {
        var review = await fixture.Runner.ReviewAsync();
        var before = Version("ORD-1003");
        await using var client = await fixture.Runner.ConnectAsync();
        var args = Cancellation(review);
        args["orderId"] = "ORD-1003";
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", args));
        Assert.Equal("INVALID_ORDER_STATE", error.Code);
        Assert.Equal(OrderStatus.Shipped, Status("ORD-1003"));
        Assert.Equal(before, Version("ORD-1003"));
    }

    [Fact]
    public async Task ReviewTokenCannotBeAppliedToAnotherOrder()
    {
        var review = await fixture.Runner.ReviewAsync();
        await using var client = await fixture.Runner.ConnectAsync();
        var args = Cancellation(review);
        args["orderId"] = "ORD-1002";
        var before = Version("ORD-1002");
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", args));
        Assert.Equal("STALE_REVIEW_CONTEXT", error.Code);
        Assert.Equal(before, Version("ORD-1002"));
    }

    [Fact]
    public async Task ChangedEvidenceInvalidatesPreviouslySignedReview()
    {
        var review = await fixture.Runner.ReviewAsync();
        fixture.Store.InTransaction(s => { var order = s.Find("ORD-1004")!; s.Save(order with { ItemsFingerprint = "different", Version = order.Version + 1 }); return true; });
        var before = Version();
        await using var client = await fixture.Runner.ConnectAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", Cancellation(review)));
        Assert.Equal("STALE_REVIEW_CONTEXT", error.Code);
        Assert.Equal(before, Version());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForgedOrExpiredReviewDoesNotWrite(bool expired)
    {
        var review = await fixture.Runner.ReviewAsync();
        var proof = fixture.Security.Verify(review.Context.ReviewToken);
        var args = Cancellation(review);
        args["reviewToken"] = expired ? fixture.Security.Sign(proof with { Expires = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() }) : "forged.review";
        var before = Version();
        await using var client = await fixture.Runner.ConnectAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", args));
        Assert.Equal(expired ? "STALE_REVIEW_CONTEXT" : "INVALID_SIGNED_CONTEXT", error.Code);
        Assert.Equal(before, Version());
    }

    [Fact]
    public async Task ImprovedRestAndMcpShareExactHandlersAndIdempotencyStore()
    {
        var review = await fixture.Runner.ReviewAsync();
        using var http = fixture.Runner.CreateHttpClient();
        var restReview = await http.GetFromJsonAsync<OrderReviewContextDto>("/capabilities/orders/ORD-1004/review-context");
        Assert.Equal(review.Context.Reasons, restReview!.Reasons);
        http.DefaultRequestHeaders.Add("Idempotency-Key", $"demo:{review.RunId}");
        using var result = await http.PostAsJsonAsync("/orders/ORD-1004/cancellation", new { reviewToken = review.Context.ReviewToken, confirmed = true });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await using var client = await fixture.Runner.ConnectAsync();
        var retry = await DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", Cancellation(review));
        Assert.True(retry.AlreadyApplied);
    }

    [Fact]
    public async Task CrudCreateUpdateDeleteWorksButCannotBypassCancellation()
    {
        using var http = fixture.Runner.CreateHttpClient();
        var input = new OrderInput("cus-contoso", "Pending", 100, "Normal", new(2026, 9, 15), "PO-CRUD", "mouse:1", "Madrid");
        using var created = await http.PostAsJsonAsync("/orders", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!;
        using var edited = await http.PutAsJsonAsync(location, input with { Amount = 110 });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var bypass = await http.PutAsJsonAsync(location, input with { Status = "Cancelled" });
        Assert.Equal(HttpStatusCode.Conflict, bypass.StatusCode);
        using var deleted = await http.DeleteAsync(location);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var protectedDelete = await http.DeleteAsync("/orders/ORD-1004");
        Assert.Equal(HttpStatusCode.Conflict, protectedDelete.StatusCode);
    }

    [Fact]
    public async Task TraceparentCorrelatesHttpProblemAcrossProcess()
    {
        using var http = fixture.Runner.CreateHttpClient();
        const string trace = "11223344556677889900aabbccddeeff";
        http.DefaultRequestHeaders.Add("traceparent", $"00-{trace}-1122334455667788-01");
        using var result = await http.GetAsync("/orders?customerId=Contoso&status=pending");
        var problem = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(trace, problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task BrowserConfirmationIsBoundToDisplayedCandidateAndDenialIsSafe()
    {
        using var http = new HttpClient { BaseAddress = new(fixture.AgentUrl) };
        using var reviewed = await http.PostAsJsonAsync("/demo/mcp/review", new { });
        var review = await reviewed.Content.ReadFromJsonAsync<ReviewRun>();
        Assert.Equal("ORD-1004", review!.Candidate.Id);
        var before = Version();
        using var denied = await http.PostAsJsonAsync("/demo/mcp/confirm", new { runId = review.RunId, confirmation = "CANCELAR ORD-1002" });
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal(before, Version());
        using var replay = await http.PostAsJsonAsync("/demo/mcp/confirm", new { runId = review.RunId, confirmation = "CANCELAR ORD-1004" });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
    }

    [Fact]
    public async Task BrowserExplicitConfirmationCancelsReviewedOrderAndKeepsTrace()
    {
        using var http = new HttpClient { BaseAddress = new(fixture.AgentUrl) };
        using var reviewed = await http.PostAsJsonAsync("/demo/mcp/review", new { });
        var review = await reviewed.Content.ReadFromJsonAsync<ReviewRun>();
        using var confirmed = await http.PostAsJsonAsync("/demo/mcp/confirm", new { runId = review!.RunId, confirmation = $"CANCELAR {review.Candidate.Id}" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var output = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(review.TraceId, output.GetProperty("traceId").GetString());
        Assert.Equal(OrderStatus.Cancelled, Status());
    }

    [Fact]
    public async Task CrossOriginWriteAndHostRebindingAreRejected()
    {
        using var http = new HttpClient { BaseAddress = new(fixture.AgentUrl) };
        http.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        using var result = await http.PostAsJsonAsync("/demo/mcp/review", new { });
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        http.DefaultRequestHeaders.Remove("Origin");
        http.DefaultRequestHeaders.Host = "evil.example";
        using var rebound = await http.PostAsJsonAsync("/demo/mcp/review", new { });
        Assert.Equal(HttpStatusCode.Forbidden, rebound.StatusCode);
    }

    [Fact]
    public async Task ResetOnlyTouchesKnownSeedIdsAndInvalidatesOldReview()
    {
        var review = await fixture.Runner.ReviewAsync();
        var custom = OrderRules.Seed()[0] with { Id = "CUSTOM-TEST", PurchaseOrderReference = "CUSTOM" };
        fixture.Store.InTransaction(s => { s.Save(custom); return true; });
        fixture.Store.ResetDemoData();
        Assert.Equal(custom, fixture.Store.InTransaction(s => s.Find(custom.Id)));
        await using var client = await fixture.Runner.ConnectAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", Cancellation(review)));
        Assert.Equal("STALE_REVIEW_CONTEXT", error.Code);
        fixture.Store.InTransaction(s => { s.Delete(custom.Id); return true; });
    }

    [Fact]
    public async Task ConcurrentRetriesAreAtomicAcrossRestAndMcp()
    {
        var review = await fixture.Runner.ReviewAsync();
        var before = Version();
        using var http = fixture.Runner.CreateHttpClient();
        http.DefaultRequestHeaders.Add("Idempotency-Key", $"demo:{review.RunId}");
        await using var client = await fixture.Runner.ConnectAsync();
        var restTask = http.PostAsJsonAsync("/orders/ORD-1004/cancellation", new { reviewToken = review.Context.ReviewToken, confirmed = true });
        var mcpTask = DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", Cancellation(review));
        using var restResponse = await restTask;
        restResponse.EnsureSuccessStatusCode();
        var rest = await restResponse.Content.ReadFromJsonAsync<CancellationResult>();
        var mcp = await mcpTask;
        Assert.NotEqual(rest!.AlreadyApplied, mcp.AlreadyApplied);
        Assert.Equal(before + 1, Version());
    }

    [Fact]
    public async Task ChangedRelatedOrderInvalidatesReview()
    {
        var review = await fixture.Runner.ReviewAsync();
        fixture.Store.InTransaction(s => { var original = s.Find("ORD-1002")!; s.Save(original with { Version = original.Version + 1 }); return true; });
        var before = Version();
        await using var client = await fixture.Runner.ConnectAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", Cancellation(review)));
        Assert.Equal("STALE_REVIEW_CONTEXT", error.Code);
        Assert.Equal(before, Version());
    }

    [Fact]
    public async Task NonDuplicateReviewNeverOffersCancel()
    {
        await using var client = await fixture.Runner.ConnectAsync();
        var context = await DemoRunner.CallAsync<OrderReviewContextDto>(client, "get_order_review_context", new() { ["orderId"] = "ORD-1001" });
        Assert.False(context.RequiresReview);
        Assert.DoesNotContain("Cancel", context.AllowedActions);
        Assert.Empty(context.ReviewToken);
        var before = Version("ORD-1001");
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order",
            new() { ["orderId"] = "ORD-1001", ["reviewToken"] = context.ReviewToken, ["confirmed"] = true, ["idempotencyKey"] = "demo:no-duplicate" }));
        Assert.Equal("INVALID_SIGNED_CONTEXT", error.Code);
        Assert.Equal(before, Version("ORD-1001"));
    }

    [Fact]
    public async Task ReadOnlyIdentityCannotReplayPreviouslyAuthorizedCancellation()
    {
        var review = await fixture.Runner.ReviewAsync();
        await fixture.Runner.ConfirmAsync(review, true);
        var version = Version();
        await using var client = await fixture.Runner.ConnectAsync(canCancel: false);
        var error = await Assert.ThrowsAsync<BusinessException>(() => DemoRunner.CallAsync<CancellationResult>(client, "cancel_order", Cancellation(review)));
        Assert.Equal("ORDER_CANCELLATION_NOT_ALLOWED", error.Code);
        Assert.Equal(version, Version());
    }

    [Fact]
    public async Task AnonymousMcpInitializationIsChallenged()
    {
        using var http = new HttpClient();
        using var response = await http.PostAsJsonAsync($"http://127.0.0.1:{new Uri(fixture.Runner.McpUrl).Port}/mcp",
            new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "test", version = "1" } } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExplicitBrowserDenialRemovesReviewAndNeverWrites()
    {
        using var http = new HttpClient { BaseAddress = new(fixture.AgentUrl) };
        using var reviewed = await http.PostAsJsonAsync("/demo/mcp/review", new { });
        var review = await reviewed.Content.ReadFromJsonAsync<ReviewRun>();
        var before = Version();
        using var denied = await http.PostAsJsonAsync("/demo/mcp/deny", new { runId = review!.RunId, confirmation = "" });
        denied.EnsureSuccessStatusCode();
        using var confirm = await http.PostAsJsonAsync("/demo/mcp/confirm", new { runId = review.RunId, confirmation = "CANCELAR ORD-1004" });
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal(before, Version());
    }

    [Fact]
    public async Task MissingReviewTokenReturnsProblemNotInternalError()
    {
        using var http = fixture.Runner.CreateHttpClient();
        http.DefaultRequestHeaders.Add("Idempotency-Key", "demo:missing-token");
        var before = Version();
        using var result = await http.PostAsJsonAsync("/orders/ORD-1004/cancellation", new { confirmed = true });
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(before, Version());
    }
}
