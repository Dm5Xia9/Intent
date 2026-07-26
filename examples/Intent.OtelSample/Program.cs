// Example: wire Intent ActivitySource + Meter into OpenTelemetry → OTLP collector.
// See docs/10-observability.md for Aspire / Jaeger steps.

using Intents;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("intent-sample"))
    .WithTracing(t => t
        .AddSource(IntentInstrumentation.Name)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")))
    .WithMetrics(m => m
        .AddMeter(IntentInstrumentation.Name)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")));

var app = builder.Build();

app.MapGet("/checkout/{orderId}", async (string orderId) =>
{
    await Intent.From(async ct =>
        {
            await Task.Delay(10, ct);
        })
        .WithNamed("Checkout")
        .WithTag("orderId", orderId)
        .WithTag("userId", "demo-user")
        .WithActivity()
        .WithMetrics();

    return Results.Ok(new { orderId });
});

app.Run();
