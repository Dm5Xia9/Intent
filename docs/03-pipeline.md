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
                                     └─ CircuitBreaker
                                          └─ Timeout
                                               └─ Retry (+ optional attemptTimeout)
                                                    └─ Bulkhead
                                                         └─ Atomic
                                                              └─ user code
```

| Политика | Order |
|----------|-------|
| Cancel | -20 |
| Named | -15 |
| Tag | -14 |
| Trace | -10 |
| Activity | -8 |
| Metrics | -5 |
| Idempotent | -4 |
| Cache | -3 |
| CircuitBreaker | -2 |
| Timeout | 0 |
| Retry | 1 |
| Bulkhead | 2 |
| Atomic | 3 |

Порядок аргументов в `Useful` не важен.

Подробно по каждой политике: **[08-policies.md](08-policies.md)**.

## Зачем так

- **Idempotent** снаружи Cache/Circuit — повтор с тем же ключом не трогает внутренний стек.
- **CircuitBreaker** снаружи Timeout/Retry — open = мгновенный fail, без ожидания timeout.
- **Cache** снаружи Circuit — cache hit не трогает circuit.
- **Timeout** снаружи Retry — общий бюджет; `attemptTimeout` внутри Retry — лимит на попытку.
- **Bulkhead** перед Atomic — сначала слот пула, потом (опционально) mutex.
- **Atomic** ближе всего к коду.

## Композиция планов

`WhenAll` / `Sequence` / `Background` / `Then` / `Select` — не политики, а фабрики/расширения над `Intent`. Политики на композите оборачивают весь агрегат. Готовые наборы: `IntentProfile.Http` / `DbWrite`.
