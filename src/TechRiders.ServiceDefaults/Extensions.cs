using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TechRiders.Application;

namespace TechRiders.ServiceDefaults;

public static class Extensions
{
    public static T AddDemoDefaults<T>(this T builder) where T : IHostApplicationBuilder
    {
        builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
        builder.Logging.AddOpenTelemetry(options => { options.IncludeScopes = true; options.IncludeFormattedMessage = true; });
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName))
            .WithTracing(t => t.AddSource(Telemetry.SourceName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.WithTracing(t => t.AddOtlpExporter()).WithMetrics(m => m.AddOtlpExporter());
            builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter());
        }
        // Keeps trace IDs usable in the logs fallback, including when the dashboard is not running.
        builder.Services.AddSingleton(new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Console.WriteLine(JsonSerializer.Serialize(new
            {
                eventName = "demo.span",
                traceId = activity.TraceId.ToString(),
                spanId = activity.SpanId.ToString(),
                parentSpanId = activity.ParentSpanId.ToString(),
                operationName = activity.OperationName,
                durationMs = activity.Duration.TotalMilliseconds,
                userId = activity.GetTagItem("user.id"),
                orderId = activity.GetTagItem("order.id"),
                idempotencyKey = activity.GetTagItem("idempotency.key")
            }))
        });
        return builder;
    }

    public static WebApplication UseDemoBoundary(this WebApplication app)
    {
        ActivitySource.AddActivityListener(app.Services.GetRequiredService<ActivityListener>());
        app.Use(async (context, next) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
            {
                context.Response.StatusCode = 403;
                return;
            }
            var host = context.Request.Host.Host;
            if (host is not ("localhost" or "127.0.0.1" or "::1" or "[::1]"))
            {
                context.Response.StatusCode = 403;
                return;
            }
            if (context.Request.Headers.TryGetValue("Origin", out var origin) &&
                (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri) ||
                 uri.Authority != context.Request.Host.Value ||
                 uri.Scheme != context.Request.Scheme))
            {
                context.Response.StatusCode = 403;
                return;
            }
            try
            {
                await next();
            }
            catch (BusinessException ex)
            {
                if (context.Response.HasStarted) throw;
                await Results.Problem(statusCode: ex.Status, title: "Invalid request", detail: ex.Message,
                    type: $"https://techriders.dev/problems/{ex.Code.ToLowerInvariant().Replace('_', '-')}",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = ex.Code,
                        ["correlationId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                        ["errors"] = new Dictionary<string, string[]> { ["request"] = [ex.Message] }
                    }).ExecuteAsync(context);
            }
        });
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy", mode = "Demo" })).AllowAnonymous();
        return app;
    }
}
