using Intents;

Console.WriteLine("=== Intent examples ===\n");

await DeferredProcessOrder();
await RetryAndTimeout();
await AtomicDemo();
await CombinedPolicies();
await CacheAndDiagnostics();
await CompositionDemo();
await IdempotentAndThenDemo();

Console.WriteLine("\nDone.");

static async Task DeferredProcessOrder()
{
    Console.WriteLine("-- Deferred async Intent --");

    async Intent ProcessOrder()
    {
        Console.WriteLine("  Validate");
        Console.WriteLine("  Save");
        Console.WriteLine("  Notify");
        await Task.Yield();
    }

    var operation = ProcessOrder();
    Console.WriteLine("  (created, not running yet)");
    await operation;
    Console.WriteLine();
}

static async Task RetryAndTimeout()
{
    Console.WriteLine("-- Retry + backoff + Timeout --");

    var attempts = 0;
    Func<Task> request = async () =>
    {
        attempts++;
        Console.WriteLine($"  Attempt {attempts}");
        await Task.Yield();
        if (attempts < 3)
            throw new InvalidOperationException("transient");
    };

    await Intent.Run(request)
        .Useful(
            Intent.Retry(3, IntentBackoff.Constant(20.Milliseconds())),
            Intent.Timeout(5.Seconds())
        );

    Console.WriteLine();
}

static async Task AtomicDemo()
{
    Console.WriteLine("-- Atomic / AtomicOn --");

    var stack = new Stack<int>();
    stack.Push(10);

    await Intent.Atomically(() =>
    {
        var value = stack.Peek();
        stack.Pop();
        Console.WriteLine($"  Popped {value}, remaining={stack.Count}");
    });

    await Intent.Atomically("demo-key", () =>
        Console.WriteLine("  Atomically(\"demo-key\")"));

    Console.WriteLine();
}

static async Task CombinedPolicies()
{
    Console.WriteLine("-- Combined policies --");

    await Intent.Run(() => Console.WriteLine("  Named + Metrics + Retry + Atomic"))
        .Useful(
            Intent.Named("CombinedDemo"),
            Intent.Metrics,
            Intent.AtomicOn("combined"),
            Intent.Retry(3),
            Intent.Timeout(10.Seconds())
        );

    Console.WriteLine($"  metrics count={IntentMetrics.GetCount("CombinedDemo")}");
    Console.WriteLine();
}

static async Task CacheAndDiagnostics()
{
    Console.WriteLine("-- Cache --");

    var calls = 0;
    Func<int> load = () =>
    {
        calls++;
        Console.WriteLine($"  loading (call #{calls})");
        return 42;
    };

    AssertEqual(42, await Intent.Run(load).Useful(Intent.Cache("answer", 5.Seconds())));
    AssertEqual(42, await Intent.Run(load).Useful(Intent.Cache("answer", 5.Seconds())));
    Console.WriteLine($"  body ran {calls} time(s)");
    Console.WriteLine();
}

static async Task CompositionDemo()
{
    Console.WriteLine("-- WhenAll / Sequence / Bulkhead --");

    await Intent.WhenAll(
        Intent.Run(() => Console.WriteLine("  WhenAll A")),
        Intent.Run(() => Console.WriteLine("  WhenAll B")));

    await Intent.Sequence(
        Intent.Run(() => Console.WriteLine("  Sequence 1")),
        Intent.Run(() => Console.WriteLine("  Sequence 2")));

    await Intent.Run(() => Console.WriteLine("  Bulkhead slot"))
        .Useful(Intent.Bulkhead("demo", 4), Intent.Named("BulkDemo"), Intent.Activity);

    Console.WriteLine();
}

static async Task IdempotentAndThenDemo()
{
    Console.WriteLine("-- Idempotent / Profile / Then --");

    var calls = 0;
    var policy = Intent.Idempotent("ex-once", 5.Seconds());
    await Intent.Run(() => { calls++; Console.WriteLine($"  idempotent body #{calls}"); }).Useful(policy);
    await Intent.Run(() => { calls++; Console.WriteLine($"  idempotent body #{calls}"); }).Useful(policy);

    await Intent.Run(() => Console.WriteLine("  IntentProfile.Http"))
        .Useful(IntentProfile.Http("ex-http"));

    var n = await Intent.Run(() => 21)
        .Then(x => Intent.Run(() => x * 2))
        .Select(x => x);
    Console.WriteLine($"  Then/Select => {n}");
    Console.WriteLine();
}

static void AssertEqual(int expected, int actual)
{
    if (expected != actual)
        throw new InvalidOperationException($"expected {expected}, got {actual}");
}
