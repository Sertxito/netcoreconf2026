using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using TechRiders.AgentDemo;
using TechRiders.Application;
using TechRiders.CopilotBridge;
using TechRiders.Domain;

namespace TechRiders.Tests;

public sealed class CopilotIntegrationTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private string SessionPath => Path.Combine(fixture.DataDirectory, "orders-session.json");

    private void WriteSession(bool canCancel = true) =>
        File.WriteAllText(SessionPath, JsonSerializer.Serialize(new LocalSession(
            fixture.ApiUrl, fixture.Runner.McpUrl, fixture.Security.IssueToken(canCancel),
            DateTimeOffset.UtcNow.AddHours(1)), DemoRunner.Json));

    private async Task<McpClient> ConnectConfiguredAsync(string configuration)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Root, configuration)));
        var server = doc.RootElement.GetProperty(configuration == ".mcp.json" ? "mcpServers" : "servers").GetProperty("orders");
        var args = server.GetProperty("args").EnumerateArray().Select(a =>
            a.GetString()!.Replace("${workspaceFolder}", fixture.Root, StringComparison.Ordinal)).ToArray();
        // Keep the production launch command; isolate only its credential file and build configuration.
        args[0] = args[0].Replace("\\Debug\\", $"\\{new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name}\\");
        args[1] = SessionPath;
        return await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = server.GetProperty("command").GetString()!,
            Arguments = args,
            WorkingDirectory = fixture.Root,
            Name = "orders"
        }), cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
    }

    [Theory]
    [InlineData(".mcp.json")]
    [InlineData(".vscode\\mcp.json")]
    public async Task ProductionConfigurationDiscoversAndCallsRealMcpWithSafeWriteAndRetry(string configuration)
    {
        fixture.Store.ResetDemoData();
        WriteSession();
        await using var bridge = await ConnectConfiguredAsync(configuration);
        var tools = await bridge.ListToolsAsync();
        Assert.Equal(["cancel_order", "get_order_review_context", "get_pending_orders"],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        await using var direct = await fixture.Runner.ConnectAsync();
        var originals = await direct.ListToolsAsync();
        foreach (var tool in tools)
            Assert.Equal(JsonSerializer.Serialize(originals.Single(t => t.Name == tool.Name).ProtocolTool),
                JsonSerializer.Serialize(tool.ProtocolTool));
        var pending = await DemoRunner.CallAsync<PendingOrderDto[]>(bridge, "get_pending_orders",
            new() { ["customerId"] = "cus-contoso" });
        var candidate = pending.OrderBy(p => p.ReviewRank).First();
        Assert.Equal("ORD-1004", candidate.Id);
        var context = await DemoRunner.CallAsync<OrderReviewContextDto>(bridge, "get_order_review_context",
            new() { ["orderId"] = candidate.Id });
        Assert.Equal("ORD-1002", Assert.Single(context.Reasons).RelatedOrderId);
        var args = new Dictionary<string, object?>
        {
            ["orderId"] = candidate.Id, ["reviewToken"] = context.ReviewToken,
            ["confirmed"] = false, ["idempotencyKey"] = $"demo:{Guid.NewGuid():N}"
        };
        var denied = await Assert.ThrowsAsync<BusinessException>(() =>
            DemoRunner.CallAsync<CancellationResult>(bridge, "cancel_order", args));
        Assert.Equal("CONFIRMATION_REQUIRED", denied.Code);
        Assert.Equal(OrderStatus.Pending, fixture.Store.InTransaction(s => s.Find(candidate.Id)!.Status));
        args["confirmed"] = true; // This test explicitly authorizes a synthetic-data mutation.
        var first = await DemoRunner.CallAsync<CancellationResult>(bridge, "cancel_order", args);
        var retry = await DemoRunner.CallAsync<CancellationResult>(bridge, "cancel_order", args);
        Assert.False(first.AlreadyApplied);
        Assert.True(retry.AlreadyApplied);
        Assert.Equal(OrderStatus.Cancelled, fixture.Store.InTransaction(s => s.Find(candidate.Id)!.Status));
        Assert.Contains(fixture.Logs, l => l.Contains("\"operationName\":\"tool.cancel_order\""));
        Assert.Contains(fixture.Logs, l => l.Contains("\"operationName\":\"CancelOrder\"") && l.Contains("\"orderId\":\"ORD-1004\""));
        Assert.Contains(fixture.Logs, l => l.Contains("\"operationName\":\"idempotency\""));
        Assert.Contains(fixture.Logs, l => l.Contains("\"operationName\":\"database.transaction\""));
        Assert.DoesNotContain(fixture.Logs, l => l.Contains(LocalSession.Read(SessionPath).Bearer, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunningBridgeReloadsCredentialsAndFailsClosedAfterSessionRemoval()
    {
        fixture.Store.ResetDemoData();
        WriteSession(false);
        await using var bridge = await ConnectConfiguredAsync(".mcp.json");
        var review = await fixture.Runner.ReviewAsync();
        var args = new Dictionary<string, object?>
        {
            ["orderId"] = review.Candidate.Id, ["reviewToken"] = review.Context.ReviewToken,
            ["confirmed"] = true, ["idempotencyKey"] = $"demo:{Guid.NewGuid():N}"
        };
        var denied = await Assert.ThrowsAsync<BusinessException>(() =>
            DemoRunner.CallAsync<CancellationResult>(bridge, "cancel_order", args));
        Assert.Equal("ORDER_CANCELLATION_NOT_ALLOWED", denied.Code);
        WriteSession();
        Assert.False((await DemoRunner.CallAsync<CancellationResult>(bridge, "cancel_order", args)).AlreadyApplied);
        File.Delete(SessionPath);
        await Assert.ThrowsAnyAsync<Exception>(() => bridge.ListToolsAsync().AsTask());
    }

    [Theory]
    [InlineData("http://example.com/mcp", false)]
    [InlineData("https://127.0.0.1/mcp", false)]
    [InlineData("http://user@127.0.0.1/mcp", false)]
    [InlineData("http://127.0.0.1/mcp?token=x", false)]
    [InlineData("http://127.0.0.1/mcp", true)]
    public void InvalidOrExpiredSessionsAreRejectedBeforeSendingCredentials(string endpoint, bool expired)
    {
        File.WriteAllText(SessionPath, JsonSerializer.Serialize(new LocalSession(
            fixture.ApiUrl, endpoint, "synthetic-test-value", DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1))));
        Assert.Throws<InvalidOperationException>(() => LocalSession.Read(SessionPath));
    }

    [Theory]
    [InlineData("/orders?customerId=cus-contoso&status=Pending", 200)]
    [InlineData("/orders?customerId=Contoso&status=pending", 400)]
    [InlineData("/swagger/v1/swagger.json", 200)]
    public async Task RestHelperUsesActualHttpAndReturnsSearchableTraceWithoutBearer(string path, int status)
    {
        fixture.Store.ResetDemoData();
        WriteSession();
        // Helpers resolve credentials relative to their script directory; use a project-local isolated copy.
        var scripts = Path.Combine(fixture.DataDirectory, "scripts");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(Path.Combine(fixture.DataDirectory, ".local"));
        File.Copy(SessionPath, Path.Combine(fixture.DataDirectory, ".local", "orders-session.json"), true);
        foreach (var file in new[] { "Local-Session.ps1", "Invoke-DemoRest.ps1" })
            File.Copy(Path.Combine(fixture.Root, "scripts", file), Path.Combine(scripts, file), true);
        var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(scripts, "Invoke-DemoRest.ps1"),
            "-Path", path }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        var output = await outputTask;
        Assert.True(process.ExitCode == 0, await errorTask);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(status, result.RootElement.GetProperty("status").GetInt32());
        if (status == 400)
            Assert.Equal("INVALID_ORDER_STATUS", result.RootElement.GetProperty("body").GetProperty("code").GetString());
        else if (path.StartsWith("/orders", StringComparison.Ordinal))
            Assert.Equal(3, result.RootElement.GetProperty("body").GetArrayLength());
        else
            Assert.True(result.RootElement.GetProperty("body").TryGetProperty("paths", out _));
        var traceId = result.RootElement.GetProperty("traceId").GetString()!;
        if (path.StartsWith("/orders", StringComparison.Ordinal))
            Assert.Contains(fixture.Logs, l => l.Contains(traceId) && l.Contains("\"operationName\":\"ListOrders\""));
        Assert.DoesNotContain(LocalSession.Read(SessionPath).Bearer, output);
    }

    [Fact]
    public async Task ProtocolCheckCommandDiscoversRealToolsAndOnlyReads()
    {
        fixture.Store.ResetDemoData();
        WriteSession();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(fixture.Root, "src", "TechRiders.CopilotBridge", "bin",
            configuration, "net10.0", "TechRiders.CopilotBridge.dll");
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = fixture.Root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var arg in new[] { dll, SessionPath, "--check" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        var output = await outputTask;
        Assert.True(process.ExitCode == 0, await errorTask);
        using var result = JsonDocument.Parse(output);
        Assert.Equal(3, result.RootElement.GetProperty("tools").GetArrayLength());
        Assert.Equal("ORD-1004", result.RootElement.GetProperty("pendingOrders")[0].GetProperty("id").GetString());
        Assert.Equal(OrderStatus.Pending, fixture.Store.InTransaction(s => s.Find("ORD-1004")!.Status));
        Assert.DoesNotContain(LocalSession.Read(SessionPath).Bearer, output);
    }
}
