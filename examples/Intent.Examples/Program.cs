using Intents;
using Intents.Examples;

Console.WriteLine("=== Intent examples ===\n");

await DeferredProcessOrder();
await RetryAndTimeout();
await AtomicDemo();
await CombinedPolicies();
await CacheAndDiagnostics();
await CompositionDemo();
await IdempotentAndThenDemo();
await CustomPolicyAndProfileDemo();

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

    await Intent.From(request)
        .WithRetry(3, IntentBackoff.Constant(20.Milliseconds()))
        .WithTimeout(5.Seconds());

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

    await Intent.From(() => Console.WriteLine("  Named + Metrics + Retry + Atomic"))
        .WithNamed("CombinedDemo")
        .WithMetrics()
        .WithAtomicOn("combined")
        .WithRetry(3)
        .WithTimeout(10.Seconds());

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

    AssertEqual(42, await Intent.From(load).WithCache("answer", 5.Seconds()));
    AssertEqual(42, await Intent.From(load).WithCache("answer", 5.Seconds()));
    Console.WriteLine($"  body ran {calls} time(s)");
    Console.WriteLine();
}

static async Task CompositionDemo()
{
    Console.WriteLine("-- WhenAll / Sequence / Bulkhead --");

    await Intent.WhenAll(
        Intent.From(() => Console.WriteLine("  WhenAll A")),
        Intent.From(() => Console.WriteLine("  WhenAll B")));

    await Intent.Sequence(
        Intent.From(() => Console.WriteLine("  Sequence 1")),
        Intent.From(() => Console.WriteLine("  Sequence 2")));

    await Intent.From(() => Console.WriteLine("  Bulkhead slot"))
        .WithBulkhead("demo", 4)
        .WithNamed("BulkDemo")
        .WithActivity();

    Console.WriteLine();
}

static async Task IdempotentAndThenDemo()
{
    Console.WriteLine("-- Idempotent / Profile / Then --");

    var calls = 0;
    await Intent.From(() => { calls++; Console.WriteLine($"  idempotent body #{calls}"); })
        .WithIdempotent("ex-once", 5.Seconds());
    await Intent.From(() => { calls++; Console.WriteLine($"  idempotent body #{calls}"); })
        .WithIdempotent("ex-once", 5.Seconds());

    await Intent.From(() => Console.WriteLine("  IntentProfile.Http"))
        .WithPolicies(IntentProfile.Http("ex-http"));

    var n = await Intent.From(() => 21)
        .Then(x => Intent.From(() => x * 2))
        .Select(x => x);
    Console.WriteLine($"  Then/Select => {n}");
    Console.WriteLine();
}

static async Task CustomPolicyAndProfileDemo()
{
    Console.WriteLine("-- Custom policy + custom profile --");

    await Intent.From(() => Console.WriteLine("  body with ConsoleLogPolicy"))
        .WithNamed("CustomPolicyDemo")
        .WithPolicies(new ConsoleLogPolicy());

    var attempts = 0;
    await Intent.From(() =>
        {
            attempts++;
            Console.WriteLine($"  worker attempt #{attempts}");
            if (attempts < 2)
                throw new InvalidOperationException("transient");
        })
        .WithPolicies(AppProfiles.Worker("ex-worker"));

    Console.WriteLine();
}

static void AssertEqual(int expected, int actual)
{
    if (expected != actual)
        throw new InvalidOperationException($"expected {expected}, got {actual}");
}
