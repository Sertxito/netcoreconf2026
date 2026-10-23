using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TechRiders.Application;
using TechRiders.Infrastructure;

namespace TechRiders.AgentDemo;

public sealed record ReviewRun(string RunId, string TraceId, string SpanId, PendingOrderDto Candidate, OrderReviewContextDto Context, string[] Tools);

public sealed class DemoRunner(DemoSecurity security, IConfiguration configuration)
{
    public const string Prompt = "Busca los pedidos pendientes de Contoso, identifica cuál debería revisar primero y cancélalo si está duplicado.";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string ApiUrl => configuration["DEMO_API_URL"] ?? "http://127.0.0.1:5101";
    public string McpUrl => configuration["DEMO_MCP_URL"] ?? "http://127.0.0.1:5102/mcp";

    public HttpClient CreateHttpClient(bool canCancel = true)
    {
        var client = new HttpClient { BaseAddress = new(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", security.IssueToken(canCancel));
        return client;
    }

    public async Task<McpClient> ConnectAsync(bool canCancel = true, CancellationToken cancellationToken = default) =>
        await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = new(McpUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {security.IssueToken(canCancel)}" }
        }), cancellationToken: cancellationToken);

    public static async Task<T> CallAsync<T>(McpClient client, string tool, Dictionary<string, object?> args, CancellationToken cancellationToken = default)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: cancellationToken);
        if (result.IsError == true) throw new InvalidOperationException($"MCP error: {JsonSerializer.Serialize(result, Json)}");
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
            throw new BusinessException(error.GetProperty("code").GetString()!, error.GetProperty("detail").GetString()!, error.GetProperty("status").GetInt32());
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidOperationException("Empty MCP result.");
    }

    public async Task<object> RestAsync(CancellationToken cancellationToken = default)
    {
        using var run = Telemetry.Source.StartActivity("agent.run.rest.teaching-failure");
        run?.SetTag("gen_ai.operation.name", "Demo (deterministic; no model)");
        using var http = CreateHttpClient();
        var openapi = await http.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json", cancellationToken);
        var operations = openapi.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(op => $"{op.Name.ToUpperInvariant()} {p.Name}")).ToArray();
        using var bad = await http.GetAsync("/orders?customerId=Contoso&status=pending", cancellationToken);
        var problem = await bad.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (bad.StatusCode != HttpStatusCode.BadRequest || problem.GetProperty("code").GetString() != "INVALID_ORDER_STATUS")
            throw new InvalidOperationException("The intentional contract failure did not return the expected 400.");
        var corrected = await http.GetFromJsonAsync<OrderDto[]>("/orders?customerId=cus-contoso&status=Pending", cancellationToken);
        return new
        {
            mode = "Demo — deterministic teaching decision, NOT a model failure",
            prompt = Prompt, operations, intentionalRequest = "GET /orders?customerId=Contoso&status=pending",
            status = (int)bad.StatusCode, problem,
            correctedRead = corrected, conclusion = "CRUD DTO contains no purchase reference, line items or delivery evidence. STOP: no cancellation and no write.",
            traceId = run?.TraceId.ToString()
        };
    }

    public async Task<ReviewRun> ReviewAsync(CancellationToken cancellationToken = default)
    {
        using var run = Telemetry.Source.StartActivity("agent.run.mcp.review");
        run?.SetTag("gen_ai.operation.name", "Demo (deterministic; no model)");
        await using var client = await ConnectAsync(cancellationToken: cancellationToken);
        var tools = (await client.ListToolsAsync(cancellationToken: cancellationToken)).Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();
        var expected = new[] { "cancel_order", "get_order_review_context", "get_pending_orders" };
        if (!tools.SequenceEqual(expected)) throw new InvalidOperationException($"Unexpected tools: {string.Join(", ", tools)}");
        var pending = await CallAsync<PendingOrderDto[]>(client, "get_pending_orders", new() { ["customerId"] = "cus-contoso" }, cancellationToken);
        var candidate = pending.OrderBy(o => o.ReviewRank).FirstOrDefault() ?? throw new BusinessException("NO_PENDING_ORDERS", "No pending orders.", 409);
        var context = await CallAsync<OrderReviewContextDto>(client, "get_order_review_context", new() { ["orderId"] = candidate.Id }, cancellationToken);
        return new(Guid.NewGuid().ToString("N"), run?.TraceId.ToString() ?? "", run?.SpanId.ToString() ?? "", candidate, context, tools);
    }

    public async Task<CancellationResult[]> ConfirmAsync(ReviewRun reviewed, bool confirmed, CancellationToken cancellationToken = default)
    {
        using var run = Telemetry.Source.StartActivity("agent.run.mcp.confirm");
        run?.SetTag("order.id", reviewed.Candidate.Id);
        if (!confirmed) throw new BusinessException("CONFIRMATION_REQUIRED", "No tool write was called: human did not confirm.", 409);
        if (!reviewed.Context.RequiresReview || !reviewed.Context.AllowedActions.Contains("Cancel") ||
            reviewed.Context.OrderId != reviewed.Candidate.Id)
            throw new BusinessException("NO_DUPLICATE", "No verified duplicate; cancellation is not allowed.", 409);
        await using var client = await ConnectAsync(cancellationToken: cancellationToken);
        var args = new Dictionary<string, object?> { ["orderId"] = reviewed.Candidate.Id,
            ["reviewToken"] = reviewed.Context.ReviewToken, ["confirmed"] = true, ["idempotencyKey"] = $"demo:{reviewed.RunId}" };
        var first = await CallAsync<CancellationResult>(client, "cancel_order", args, cancellationToken);
        var retry = await CallAsync<CancellationResult>(client, "cancel_order", args, cancellationToken);
        if (first.OrderId != reviewed.Candidate.Id || first.AlreadyApplied || !retry.AlreadyApplied)
            throw new InvalidOperationException("Idempotent retry did not match expected behavior.");
        return [first, retry];
    }
}
