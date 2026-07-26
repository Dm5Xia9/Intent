# Pipeline политик

## Порядок (снаружи → внутрь)

```
Cancel
  └─ Named
       └─ Tag
            └─ Trace
                 └─ Activity
                      └─ Metrics
                           └─ Idempotent
                                └─ Cache
                                     └─ CircuitBreaker   ← Intent.Polly
                                          └─ Timeout     ← Intent.Polly
                                               └─ Retry  ← Intent.Polly
                                                    └─ Bulkhead ← Intent.Polly
                                                         └─ Atomic
                                                              └─ Before
                                                                   └─ After
                                                                        └─ user code
```

| Политика | Order | Пакет |
|----------|-------|-------|
| Cancel | -20 | core |
| Named | -15 | core |
| Tag | -14 | core |
| Trace | -10 | core |
| Activity | -8 | core |
| Metrics | -5 | core |
| Idempotent | -4 | core |
| Cache | -3 | core |
| CircuitBreaker | -2 | Intent.Polly |
| Timeout | 0 | Intent.Polly |
| Retry | 1 | Intent.Polly |
| Bulkhead | 2 | Intent.Polly |
| Atomic | 3 | core |
| Before | 4 | core |
| After | 5 | core |

Порядок аргументов в `With*` / `Configure` не важен.

**Стабильность.** Числовые `Order` зафиксированы в `IntentPipelineOrder`. Менять порядок — breaking change (до 1.0 только с записью в [CHANGELOG](../CHANGELOG.md); с 1.0 — major bump).

Подробно по каждой политике: **[08-policies.md](08-policies.md)**.

## Зачем так

- **Idempotent** снаружи Cache/Circuit — повтор с тем же ключом не трогает внутренний стек.
- **CircuitBreaker** снаружи Timeout/Retry — open = мгновенный fail, без ожидания timeout.
- **Cache** снаружи Circuit — cache hit не трогает circuit.
- **Timeout** снаружи Retry — общий бюджет; `attemptTimeout` внутри Retry — лимит на попытку.
- **Bulkhead** перед Atomic — сначала слот пула, потом (опционально) mutex.
- **Atomic** перед Before/After — хуки и тело под одним mutex (если Atomic включён).
- **Before / After** внутри Retry — на **каждую** попытку тела; After в `finally` (и при ошибке).

## Композиция планов

`WhenAll` / `Sequence` / `Background` / `Into` / `FromEach` / `Then` / `Select` — не политики, а фабрики/расширения над `Intent`. Политики на композите оборачивают весь агрегат. Готовые наборы: `IntentPolly.Http` / `DbWrite`.
