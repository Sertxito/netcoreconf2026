using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry.Trace;
using TechRiders.AgentDemo;
using TechRiders.Application;
using TechRiders.Infrastructure;
using TechRiders.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddDemoDefaults();
builder.Services.AddBusiness(builder.Configuration);
builder.Services.AddSingleton<DemoRunner>();
var app = builder.Build();
app.UseDemoBoundary();
var runner = app.Services.GetRequiredService<DemoRunner>();

if (args.Any(a => a.StartsWith("--", StringComparison.Ordinal) && a is "--rest" or "--mcp" or "--rehearse" or "--reset" or "--token" or "--live"))
{
    try
    {
        _ = app.Services.GetService<TracerProvider>();
        if (args.Contains("--live"))
            throw new InvalidOperationException("Live mode is not configured. No model was invoked. Demo is the supported offline mode; Microsoft.Extensions.AI is referenced for a future explicit provider.");
        if (args.Contains("--token"))
        {
            Console.WriteLine(app.Services.GetRequiredService<DemoSecurity>().IssueToken());
            return;
        }
        if (args.Contains("--reset"))
        {
            if (!args.Contains("--confirm")) throw new InvalidOperationException("Reset requires --reset --confirm. Only the four demo seed IDs and demo: idempotency keys are reset.");
            app.Services.GetRequiredService<SqliteOrderStore>().ResetDemoData();
            Console.WriteLine("Demo reset: ORD-1001..ORD-1004; versions advanced; demo: idempotency keys cleared. Other orders preserved.");
            return;
        }
        using var intention = Telemetry.Source.StartActivity("user.request");
        intention?.SetTag("demo.mode", "Demo");
        Console.WriteLine($"DEMO OFFLINE — no LLM, real HTTP/MCP\n{DemoRunner.Prompt}\nTRACE_ID={intention?.TraceId}");
        if (args.Contains("--rest") || args.Contains("--rehearse"))
            Console.WriteLine(JsonSerializer.Serialize(await runner.RestAsync(), DemoRunner.Json));
        if (args.Contains("--mcp") || args.Contains("--rehearse"))
        {
            var review = await runner.ReviewAsync();
            Console.WriteLine(JsonSerializer.Serialize(review, DemoRunner.Json));
            Console.WriteLine($"Propuesta: cancelar EXACTAMENTE {review.Candidate.Id}; relacionado con {string.Join(", ", review.Context.Reasons.Select(r => r.RelatedOrderId))}.");
            var confirmed = args.Contains("--confirm");
            if (!confirmed && !Console.IsInputRedirected)
            {
                Console.Write($"Escribe CANCELAR {review.Candidate.Id} para confirmar (cualquier otra entrada deniega): ");
                confirmed = Console.ReadLine() == $"CANCELAR {review.Candidate.Id}";
            }
            if (!confirmed) Console.WriteLine("DENEGADO / SIN CONFIRMACIÓN: no se ha llamado a cancel_order.");
            else Console.WriteLine(JsonSerializer.Serialize(await runner.ConfirmAsync(review, true), DemoRunner.Json));
        }
        Console.WriteLine($"TRACE_ID={intention?.TraceId}; buscar en Aspire > Traces (o logs JSON).");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Demo failed explicitly: {ex.Message}");
        Environment.ExitCode = 1;
    }
    finally
    {
        app.Services.GetService<TracerProvider>()?.ForceFlush();
        await app.DisposeAsync();
    }
    return;
}

var reviews = new ConcurrentDictionary<string, ReviewRun>();
app.MapGet("/", () => Results.Content(Page.Html, "text/html; charset=utf-8"));
app.MapPost("/demo/rest", async (CancellationToken ct) => await runner.RestAsync(ct));
app.MapPost("/demo/mcp/review", async (CancellationToken ct) =>
{
    using var intention = Telemetry.Source.StartActivity("user.request");
    var reviewed = await runner.ReviewAsync(ct);
    reviews[reviewed.RunId] = reviewed;
    return reviewed;
});
app.MapPost("/demo/mcp/confirm", async (ConfirmationInput body, CancellationToken ct) =>
{
    if (!reviews.TryRemove(body.RunId, out var reviewed))
        throw new BusinessException("UNKNOWN_REVIEW", "Review again; confirmation must match a current displayed run.", 409);
    if (body.Confirmation != $"CANCELAR {reviewed.Candidate.Id}")
        throw new BusinessException("CONFIRMATION_REQUIRED", "Confirmation denied. No cancellation called.", 409);
    var parent = new ActivityContext(ActivityTraceId.CreateFromString(reviewed.TraceId),
        ActivitySpanId.CreateFromString(reviewed.SpanId), ActivityTraceFlags.Recorded);
    using var run = Telemetry.Source.StartActivity("user.confirmation", ActivityKind.Internal,
        parentContext: parent);
    return new { traceId = run?.TraceId.ToString(), reviewTraceId = reviewed.TraceId, results = await runner.ConfirmAsync(reviewed, true, ct) };
});
app.MapPost("/demo/mcp/deny", (ConfirmationInput body) => new { removed = reviews.TryRemove(body.RunId, out _), result = "Denied; no cancellation." });
app.Run();

public sealed record ConfirmationInput(string RunId, string Confirmation);
