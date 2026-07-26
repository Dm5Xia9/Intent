# Контракт state machine clone (Retry)

Каждая попытка Retry (и любой повторный вызов body) для `async Intent` / `async Intent<T>`
запускает **клон** ещё не стартовавшей compiler state machine (`MemberwiseClone` шаблона).
Шаблон, сохранённый при создании плана, **никогда** не получает `MoveNext`.

## Что можно

| Паттерн | Почему безопасно |
|---------|------------------|
| Параметры метода (`orderId`, `ct`, …) | Поля SM копируются в клон; каждая попытка видит те же аргументы вызова |
| Локальные переменные до первого `await` | Инициализируются заново при `MoveNext` клона |
| `await Task` / `await Intent` внутри тела | Продолжения идут на клоне текущей попытки |
| `From(async ct => …)` + Cancel/Timeout | `ct` — linked token из pipeline; при timeout он отменяется |
| Чистые captures (immutable / readonly snapshot) | Клон копирует ссылку; если объект не мутируется между попытками — ок |

```csharp
async Intent Charge(string paymentId)
{
    // paymentId — capture параметра: одинаков на каждой попытке Retry
    await Gateway.ChargeAsync(paymentId, Intent.CurrentCancellationToken);
}

await Charge(id).WithRetry(3);
```

## Что ломает Retry / даёт сюрпризы

| Паттерн | Почему опасно |
|---------|----------------|
| Мутация shared-объекта, захваченного извне | Все попытки делят одну ссылку; частичный side effect остаётся |
| Статические / синглтон-счётчики без учёта повторов | «Попытка 2» видит уже изменённое состояние |
| Side effect **до** первого await без компенсации | При retry эффект повторится (или останется полу-применённым) |
| Замыкание на `ref` / mutable struct снаружи | Клон копирует значение на момент создания плана, не «текущее» |
| Ожидание, что timeout **убивает** поток | Timeout/Cancel — cooperative; код без проверки token может бежать дальше |
| `Configure` после первого `await` / `Schedule` | Бросает; план уже зафиксирован |

```csharp
// Плохо: shared list растёт на каждой попытке
var log = new List<string>();
async Intent Bad()
{
    log.Add("attempt"); // side effect на shared capture
    throw new InvalidOperationException();
}
```

```csharp
// Лучше: идемпотентный side effect или эффект только после успеха
async Intent Good(string key)
{
    await Intent.From(() => ChargeOnce(key))
        .WithIdempotent($"pay:{key}");
}
```

## Double-await

Повторный `await` того же экземпляра **не** перезапускает pipeline: срабатывает schedule gate,
второй awaiter присоединяется к тому же `TaskCompletionSource`. Для нового прогона создайте
новый Intent (новый вызов метода / `From`).

## Nested `await Intent`

Вложенный Intent — отдельный план со своими политиками. Политики родителя **не** наследуются
автоматически. Retry родителя заново выполнит тело родителя, включая повторный `await child`
(если child уже Completed — join без повторного body child).

## From vs async Intent

| | `From(delegate)` | `async Intent` |
|--|------------------|----------------|
| Retry | Повторный вызов того же делегата | Клон SM-шаблона |
| Cancel token | Параметр `ct` у `From(async ct => …)` | `Intent.CurrentCancellationToken` или параметр метода |
| `From(Action)` / `From(Func<Task>)` | Token почти не используется (только `ThrowIfCancellationRequested` у sync) | — |

## Cancel / Timeout (семантика)

1. **Cancel** — linked CTS: внешний token + входящий pipeline token → в `next` и в `Intent.CurrentCancellationToken`.
2. **Timeout** (`Intent.Polly`) — `CancelAfter` на linked CTS **и** `WaitAsync` (wall-clock). Тело, которое слушает token, останавливается cooperatively; тело, которое игнорирует token, может продолжить фон после возврата WaitAsync.
3. Корневой запуск pipeline — `CancellationToken.None`; без Cancel/Timeout тело не отменяется снаружи.

Жёсткое правило: **abort потоков нет**. I/O должен принимать `CancellationToken`.
