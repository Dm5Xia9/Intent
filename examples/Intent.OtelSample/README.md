# Intent + OpenTelemetry sample

Minimal ASP.NET app that registers `IntentInstrumentation.Name` for traces and metrics and exports OTLP to `localhost:4317`.

## Run with Jaeger (all-in-one)

```bash
docker run --rm -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one:1.57
dotnet run --project examples/Intent.OtelSample
curl http://localhost:5000/checkout/42
# open http://localhost:16686 → service intent-sample → operation Checkout
```

## Run with collector config

See [otel-collector-config.yaml](../otel-collector-config.yaml) and [docs/10-observability.md](../../docs/10-observability.md).
