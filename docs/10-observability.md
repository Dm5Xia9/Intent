# Observability: Activity, Metrics, correlation

Intent пишет в стандартные .NET API: `ActivitySource` и `System.Diagnostics.Metrics`.

| Сигнал | API | Стабильное имя |
|--------|-----|----------------|
| Spans | `ActivitySource` | **`Intents.Intent`** |
| Metrics | `Meter` | **`Intents.Intent`** |
| Лёгкий debug-trace | `IntentDiagnostics.Traced` | in-process event |

Константы и инструменты: `IntentInstrumentation`.

```csharp
IntentInstrumentation.Name;                      // "Intents.Intent"
IntentInstrumentation.ExecutionCountInstrument;  // "intents.execution.count"
IntentInstrumentation.ExecutionDurationInstrument; // "intents.execution.duration"
```

## Политики

```csharp
await Work()
    .WithNamed("Checkout")
    .WithTag("userId", userId)
    .WithTag("orderId", orderId)
    .WithActivity()   // span + tags + baggage
    .WithMetrics()    // counter + histogram
    .WithTrace();     // optional IntentDiagnostics.Traced
```

**Tag → Activity:** каждый ambient tag становится:

1. **Span tag** (`activity.SetTag`) — видно на span в Jaeger / Aspire.
2. **Baggage** (`activity.SetBaggage`) — едет в W3C baggage / OTel baggage к downstream.

**Metrics tags:** `intent.name`, `intent.outcome` (`success`|`failure`) + ambient tags (кроме reserved).

## Aspire Dashboard

В AppHost / service:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource(IntentInstrumentation.Name)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(m => m
        .AddMeter(IntentInstrumentation.Name)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation());

builder.Services.AddServiceDiscovery();
// Aspire wiring usually: builder.AddServiceDefaults() already includes OTel.
```

В Dashboard:

1. **Traces** — ищите spans с source `Intents.Intent`, name = `WithNamed(...)`.
2. Parent HTTP/request span должен быть родителем Intent span (Activity наследует `Activity.Current`).
3. В attributes span — `userId`, `orderId`, …
4. **Metrics** — `intents.execution.count`, `intents.execution.duration` с dimension `intent.name`.

## OpenTelemetry Collector → Jaeger

Минимальный `appsettings` / programmatic:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("checkout-api"))
    .WithTracing(t => t
        .AddSource(IntentInstrumentation.Name)
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")))
    .WithMetrics(m => m
        .AddMeter(IntentInstrumentation.Name)
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")));
```

Пример `otel-collector-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

processors:
  batch:

exporters:
  otlp/jaeger:
    endpoint: jaeger:4317
    tls:
      insecure: true
  # debug: # optional
  #   verbosity: basic

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlp/jaeger]
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlp/jaeger]  # or prometheusremotewrite / otlphttp
```

Docker Compose (фрагмент):

```yaml
services:
  jaeger:
    image: jaegertracing/all-in-one:1.57
    ports: ["16686:16686", "4317:4317"]

  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.96.0
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports: ["4317:4317"]
```

Приложение → OTLP `:4317` → collector → Jaeger UI `http://localhost:16686`.

### Как увидеть correlation в Jaeger

1. Вызовите endpoint, который делает `.WithNamed("Checkout").WithTag("userId","u1").WithActivity()`.
2. В Jaeger найдите service `checkout-api`, operation `Checkout` (или parent HTTP + child `Checkout`).
3. Откройте span `Checkout`:
   - **Tags:** `userId=u1`, …
   - **Process/Service** — ваш resource.
4. Если downstream HTTP client использует OTel instrumentation, baggage `userId` может появиться на child spans (зависит от propagator; по умолчанию W3C baggage).

Проверка parent-child: у Intent span `References` → `CHILD_OF` входящего ASP.NET span.

## Без Aspire (локальный MeterListener)

```csharp
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == IntentInstrumentation.Name)
        l.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((inst, measurement, tags, _) =>
{
    Console.WriteLine($"{inst.Name} += {measurement}");
});
listener.SetMeasurementEventCallback<double>((inst, measurement, tags, _) =>
{
    Console.WriteLine($"{inst.Name} = {measurement} ms");
});
listener.Start();
```

## Что не делать

- Не опирайтесь только на `IntentDiagnostics.Traced` в проде — для метрик и трейсов используйте OTel / Aspire / Prometheus (`IntentInstrumentation`).

Пример приложения: [`examples/Intent.OtelSample`](../examples/Intent.OtelSample).
