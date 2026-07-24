# Внутреннее устройство

## Слои

```
async Intent Method()
        │
        ▼
IntentMethodBuilder   ◄── компилятор (AsyncMethodBuilder attribute)
        │
        ▼
Intent (план + policies + TCS + optional SM)
        │
        ▼  await / Schedule
RunPipelineAsync
        │
        ▼
IntentPipeline.Build  →  Timeout(Retry(Atomic(body)))
        │
        ▼
body: делегат  или  ExecuteStateMachineAsync → SM.MoveNext()
        │
        ▼
TaskCompletionSource  →  внешний IntentAwaiter
```

## Custom AsyncMethodBuilder

Атрибут на типе:

```csharp
[AsyncMethodBuilder(typeof(IntentMethodBuilder))]
public class Intent { ... }
```

Компилятор для `async Intent` генерирует вызовы:

| Метод builder’а | Роль у Intent |
|-----------------|---------------|
| `Create` | Создать `Intent` сразу (shared reference) |
| `Start` | Забоксить SM, `BindStateMachine`, **без** `MoveNext` |
| `Task` | Вернуть тот же `Intent` |
| `SetResult` / `SetException` | Сигнал завершения SM (через `_smRun`) |
| `AwaitOnCompleted` / `Unsafe…` | Продолжения SM на обычных awaitable |

Ключевой трюк: builder — **struct**, но поле `_intent` — **класс**. Все копии builder’а (на стеке и внутри SM) указывают на один объект Intent. Иначе `SetResult` попал бы в другой экземпляр, чем тот, что вернули вызывающему.

## Два вида body

### 1. Делегат (`Run` / `Defer`)

```csharp
_body = ct => { action(); return Task.CompletedTask; };
```

Можно вызывать многократно → `Retry` работает напрямую.

### 2. State machine (`async Intent`)

```csharp
BindStateMachine(template); // шаблон, MoveNext на нём не вызывается
_body = ExecuteStateMachineAsync;
```

`ExecuteStateMachineAsync` на **каждую** попытку (в том числе Retry):

1. Клонирует шаблон (`MemberwiseClone` бокса SM).
2. Создаёт `_smRun` (локальный TCS «прогон SM»).
3. `clone.MoveNext()`.
4. Ждёт `_smRun`.

Так `await Flaky().Useful(Intent.Retry(3))` работает без `Defer`: каждая попытка — свежая state machine с capturenными аргументами исходного вызова.

Когда SM доходит до конца, builder зовёт `SetResult`/`SetException` → завершается `_smRun`, а не сразу внешний TCS. Внешний TCS завершает `RunPipelineAsync` после выхода из pipeline (успех) или через `FaultOuter` (ошибка).

Зачем два уровня TCS:

- pipeline (Timeout/Retry) должен видеть **завершение попытки** как обычный `Task`;
- внешний awaiter не должен завершиться в середине Retry.

## Schedule и гонки

```csharp
if (Interlocked.CompareExchange(ref _scheduleGate, 1, 0) != 0)
    return;
```

Двойной `await` одного Intent не запускает pipeline дважды. Второй await просто присоединяется к тому же TCS.

## Awaitable снаружи

```csharp
public IntentAwaiter GetAwaiter()
{
    Schedule();
    return new IntentAwaiter(this);
}
```

`IntentAwaiter` делегирует в TCS:

- `IsCompleted`
- `OnCompleted` / `UnsafeOnCompleted`
- `GetResult` (с `ExceptionDispatchInfo`, если есть)

Для внешнего кода `await intent` выглядит как await любого кастомного awaitable.

## Политики и CancellationToken

`Wrap` принимает `CancellationToken`. Сейчас корневой вызов — `CancellationToken.None`.  
`Timeout` использует `Task.WaitAsync(timeout)`, а не только cooperative cancel тела. Тело может продолжать фоновую работу после timeout на уровне ожидания — это ограничение MVP (как у многих timeout-обёрток без abort).

## Atomic

Один процессный `SemaphoreSlim(1,1)` на все `Intent.Atomic`. Это **не** распределённый лок и не per-resource lock. Для разных ресурсов в будущем понадобятся ключи/scopes.

## Производительность (ожидания)

- Холодный Intent дешевле «уже запущенного» лишнего Task только если работу реально откладывают или отменяют до await.
- Лишние аллокации: сам Intent, список политик, TCS, при async — box SM.
- Для hot path без политик обычно выгоднее обычный `Task`.

Intent оптимизирован под **ясность композиции**, не под наносекунды.
