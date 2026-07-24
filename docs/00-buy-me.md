# Intent: бизнес-код отдельно — режим исполнения отдельно

Ты уже сто раз писал это.

Retry внутри метода. Timeout снаружи. Лок ещё чуть выше. Circuit breaker в другом сервисе. Идемпотентность «на честном слове» в контроллере. Метрики — если не забыли.

Один и тот же `Checkout` нельзя спокойно вызвать «просто из UI» и «с ретраями из воркера» — без копипасты или обёрток, которые душат читаемость.

**Intent** — это `async`/`await`, который стартует не при вызове, а при `await`. А между ними ты говоришь *как* выполнять.

Пишется как обычный метод: `async Intent` вместо `async Task`, внутри те же `await`, те же шаги. Меняется только тип возврата и момент старта.

```csharp
async Intent Checkout(Cart cart)
{
    await Validate(cart);
    await Charge(cart);
    await Ship(cart);
}

var plan = Checkout(cart);                    // ещё ничего не произошло
await plan.Useful(IntentProfile.Http("checkout"), Intent.Idempotent(key));
```

Один метод. Разные режимы. Ноль `try/catch` вокруг бизнес-логики ради инфраструктуры.

---

## Киллер-фичи, о которых ты давно мечтал

### 1. Cold by default — вызов ≠ старт

`async Task` горит в момент вызова. HTTP уже улетел, пока ты «только собирался» навесить timeout.

```csharp
var t = ChargeAsync(cmd);     // уже в полёте
await t;
```

С Intent тело спит, пока ты не готов:

```csharp
var op = Charge(cmd);         // холодный план
op = op.Useful(Intent.Timeout(5.Seconds()), Intent.Retry(3));
await op;                     // вот теперь Charge реально бежит
```

Конфигурация *до* исполнения. Как и должно быть.

### 2. `Useful(...)` — политики снаружи, порядок сам

Не builder-лапша. Не `ExecuteAsync(() => ...)`. Просто:

```csharp
await PlaceOrder(cmd).Useful(
    Intent.Named("PlaceOrder"),
    Intent.Tag("userId", cmd.UserId),
    Intent.Idempotent($"order:{cmd.Key}"),
    Intent.CircuitBreaker("orders"),
    Intent.Retry(3, attemptTimeout: 2.Seconds()),
    Intent.Timeout(10.Seconds()),
    Intent.Cancel(ct)
);
```

Порядок аргументов **не важен** — pipeline сам выстраивает Cancel → … → Retry → Atomic → твой код. Ты думаешь о *смысле*, не о вложенности onion-wrapper’ов.

### 3. Один `Checkout` — три жизни

```csharp
async Intent Checkout(Cart cart) { /* validate → pay → ship — как в async Task */ }

await Checkout(cart);                          // UI: быстро, без церемоний
await Checkout(cart).Useful(IntentProfile.Http("checkout"));
await Checkout(cart).Useful(                   // платежи: жёсткий контракт
    Intent.Idempotent(key),
    Intent.AtomicOn($"cart:{cart.Id}"),
    Intent.Retry(5),
    Intent.Timeout(30.Seconds()));
```

Бизнес-метод не знает про Polly, семафоры и ActivitySource. Вызывающий выбирает профиль — как линзу поверх одной и той же операции.

### 4. Идемпотентность, которая не врёт

Проблема не в «забыли проверить ключ в контроллере». Она в том, что **retry и повторный запрос — разные вещи**, а без общего ключа runtime их не различает:

- `Retry` после сетевого сбоя может вторым заходом снова дернуть `Charge`, хотя первый уже прошёл у провайдера.
- Клиент жмёт «оплатить» дважды — два независимых `await Charge(...)`.
- Два воркера снимают одну команду с очереди — два параллельных run’а.

Обычно это чинят руками: словарь в памяти, Redis, «надеюсь, уникальный индекс в БД спасёт». Либо ключ проверяют *до* вызова, а retry живёт *внутри* — и дырка остаётся между ними.

`Idempotent` вешает контракт на сам plan: один ключ на всю операцию, включая retry и параллельные await’ы.

```csharp
await Charge(cmd).Useful(
    Intent.Idempotent($"pay:{cmd.IdempotencyKey}"),
    Intent.Retry(3));
```

Что происходит по ключу:

1. **Первый заход** — тело бежит как обычно; при успехе результат (или факт завершения) запоминается на TTL.
2. **Повтор с тем же ключом, пока ещё success в TTL** — тело **не** вызывается; для `Intent<T>` отдаётся сохранённый результат.
3. **Параллельный await с тем же ключом** — не стартует второй `Charge`, а **ждёт тот же in-flight run** и получает тот же исход.
4. **Падение** — ключ не «залипает»: следующий вызов может пойти снова. Запоминается только успех.

«Не врёт» = это не комментарий в коде и не проверка на входе, а поведение pipeline: side effect с данным ключом либо один раз доводится до успеха, либо честно переигрывается после ошибки — без тихого второго списания из‑за retry/двойного клика в том же процессе.

### 5. Resilience-пакет без церемоний

То, что обычно собирают из пяти NuGet и трёх wiki-страниц:

| Мечта | Как |
|-------|-----|
| Не долбить мёртвый сервис | `CircuitBreaker` |
| Не убить downstream толпой | `Bulkhead` |
| Бюджет на всё + лимит на попытку | `Timeout` + `Retry(..., attemptTimeout:)` |
| Критическая секция без `lock` в домене | `Atomic` / `AtomicOn` |
| Готовый HTTP-профиль | `IntentProfile.Http("payments")` |

```csharp
await CallPayments(req).Useful(IntentProfile.Http("payments"));
await SaveOrder(order).Useful(IntentProfile.DbWrite("orders"));
```

### 6. Наблюдаемость из коробки, не «потом добавим»

```csharp
await Work().Useful(
    Intent.Named("Fulfillment"),
    Intent.Tag("orderId", id),
    Intent.Activity,   // OpenTelemetry ActivitySource
    Intent.Trace,      // лёгкие lifecycle-события
    Intent.Metrics);   // latency / success / fail
```

Имя, теги, span, метрики — без протаскивания `ILogger` через каждый слой «на всякий случай».

### 7. Композиция планов, а не ада колбэков

```csharp
await Intent.WhenAll(ReserveStock(), AuthorizeCard());
await Intent.Sequence(Validate(), Persist(), Notify());

var total = await Load(id)
    .Then(x => Enrich(x))
    .Select(x => x.Total);

RiskyWork().Useful(Intent.Retry(2)).Background();
```

Это всё ещё обычный C#: `await`, generic’и, привычный mental model. Просто операция — **значение**, с которым можно работать до старта.

---

## Было / стало

| Раньше | С Intent |
|--------|----------|
| Политики внутри метода или снаружи в обёртке | `Useful` на холодном плане |
| Retry копипастой / отдельным Polly-execute | `Intent.Retry` в том же await |
| Идемпотентность «надеюсь, контроллер проверит» | `Intent.Idempotent(key)` |
| Лок рядом с доменной логикой | `AtomicOn` снаружи |
| Метрики — если вспомнили | `Named` + `Metrics` / `Activity` |
| «Как бы вызвать тот же метод иначе» | Тот же метод + другой набор политик |

---

## Одна картинка в голове

```
Ты:     что сделать          →  async Intent Checkout(...)
Intent: как выполнить        →  .Useful(Idempotent, Retry, Timeout, …)
CLR:    когда пошло          →  await
```

Не scheduler. Не saga-engine. Не «ещё один MediatR».

Тонкий слой над awaitables: **cold execution + policy pipeline** — ровно то место, где инфраструктура годами протекала в домен.

---

## Дальше

Уже зацепило? Тогда по делу:

1. [Идея и границы](01-overview.md) — что это и чем не является  
2. [Жизненный цикл](02-lifecycle.md) — Created → Completed  
3. [Все политики](08-policies.md) — подробно, с подводными камнями  
4. [Кейсы](06-use-cases.md) — когда брать, когда не надо  
5. [API](07-api.md) — шпаргалка  

```bash
dotnet test
dotnet run --project examples/Intent.Examples
```

Напиши операцию один раз. Режимы исполнения — навсегда снаружи.
