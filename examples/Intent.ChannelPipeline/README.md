# Intent.ChannelPipeline

Пример fan-in / multi-stage pipeline через `System.Threading.Channels`, **`Into`** (produce) и **`FromEach`** (consume).

```
catalog ──┐
pricing ──┼─ Into(fragments) ─► join ─► enriched ─FromEach(Score)─► ranked ─FromEach─► print
reviews ──┘
```

Для каждого SKU три источника стартуют в фоне с `WithRetry` / `WithTimeout` и пишут в канал через `.Into`. Join собирает карточку; `FromEach` на каждом enriched считает score и прокидывает в `ranked`; ещё один `FromEach` собирает строки для печати. `sku-404` специально падает на catalog.

```bash
dotnet run --project examples/Intent.ChannelPipeline
```
