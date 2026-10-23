using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using TechRiders.CopilotBridge;

var check = args.Contains("--check", StringComparer.Ordinal);
var sessionFile = Path.GetFullPath(args.Where(a => a != "--check").SingleOrDefault()
    ?? Path.Combine(".local", "orders-session.json"));
if (check)
{
    await using var client = await LocalSession.ConnectAsync(sessionFile);
    var tools = await client.ListToolsAsync();
    if (!tools.Select(t => t.Name).Order(StringComparer.Ordinal)
        .SequenceEqual(new[] { "cancel_order", "get_order_review_context", "get_pending_orders" }))
        throw new InvalidOperationException("The local server did not advertise the expected three tools.");
    var pending = await client.CallToolAsync("get_pending_orders",
        new Dictionary<string, object?> { ["customerId"] = "cus-contoso" });
    if (pending.IsError == true) throw new InvalidOperationException("MCP read failed.");
    using var result = JsonDocument.Parse(string.Join("\n", pending.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    if (result.RootElement.ValueKind != JsonValueKind.Array)
        throw new InvalidOperationException("MCP read did not return pending orders.");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode = "Protocol check only; no Copilot/model and no writes",
        tools = tools.Select(t => t.Name),
        pendingOrders = result.RootElement
    }));
    return;
}
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport()
    .WithListToolsHandler(async (_, ct) =>
    {
        await using var client = await LocalSession.ConnectAsync(sessionFile, ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        return new ListToolsResult { Tools = tools.Select(t => t.ProtocolTool).ToList() };
    })
    .WithCallToolHandler(async (request, ct) =>
    {
        var parameters = request.Params ?? throw new InvalidOperationException("Missing tool parameters.");
        await using var client = await LocalSession.ConnectAsync(sessionFile, ct);
        return await client.CallToolAsync(parameters.Name,
            parameters.Arguments?.ToDictionary(p => p.Key, p => (object?)p.Value),
            cancellationToken: ct);
    });
await builder.Build().RunAsync();
