using System.Collections.Concurrent;
using System.Threading.Channels;
using Intents;

namespace Intents.ChannelPipeline;

internal static class Sources
{
    public static async Intent<SourceFragment> FetchCatalog(string productId)
    {
        await DelaySource();
        if (productId.EndsWith("404", StringComparison.Ordinal))
            throw new InvalidOperationException($"catalog miss: {productId}");

        return new SourceFragment(
            productId,
            SourceKind.Catalog,
            Title: $"Widget {productId[^3..]}",
            Price: null,
            Rating: null);
    }

    public static async Intent<SourceFragment> FetchPricing(string productId)
    {
        await DelaySource();
        var price = 10m + Random.Shared.Next(5, 40);
        return new SourceFragment(productId, SourceKind.Pricing, Title: null, Price: price, Rating: null);
    }

    public static async Intent<SourceFragment> FetchReviews(string productId)
    {
        await DelaySource();
        var rating = Math.Round(2.5 + Random.Shared.NextDouble() * 2.5, 1);
        return new SourceFragment(productId, SourceKind.Reviews, Title: null, Price: null, Rating: rating);
    }

    public static async Intent<RankedProduct> Score(EnrichedProduct product)
    {
        await Task.Yield();
        var score = Math.Round((double)(100m / product.Price) * product.Rating, 2);
        return new RankedProduct(product, score);
    }

    private static Task DelaySource() =>
        Task.Delay(Random.Shared.Next(40, 160), Intent.CurrentCancellationToken);
}

internal static class Pipeline
{
    public static async Task JoinAsync(
        ChannelReader<SourceFragment> input,
        ChannelWriter<EnrichedProduct> output,
        int expectedProducts,
        CancellationToken ct)
    {
        var bags = new ConcurrentDictionary<string, Partial>();
        var completed = 0;

        try
        {
            await foreach (var frag in input.ReadAllAsync(ct))
            {
                var partial = bags.GetOrAdd(frag.ProductId, static _ => new Partial());
                EnrichedProduct? ready = null;

                lock (partial)
                {
                    switch (frag.Kind)
                    {
                        case SourceKind.Catalog:
                            partial.Title = frag.Title;
                            break;
                        case SourceKind.Pricing:
                            partial.Price = frag.Price;
                            break;
                        case SourceKind.Reviews:
                            partial.Rating = frag.Rating;
                            break;
                    }

                    if (partial.Title is not null && partial.Price is not null && partial.Rating is not null)
                    {
                        ready = new EnrichedProduct(
                            frag.ProductId,
                            partial.Title,
                            partial.Price.Value,
                            partial.Rating.Value);
                    }
                }

                if (ready is null)
                    continue;

                Console.WriteLine($"  joined {ready.Value.ProductId}: {ready.Value.Title}");
                await output.WriteAsync(ready.Value, ct);

                if (Interlocked.Increment(ref completed) >= expectedProducts)
                    break;
            }
        }
        finally
        {
            output.TryComplete();
        }
    }

    public static void PrintTable(IReadOnlyList<RankedProduct> rows)
    {
        Console.WriteLine("\nRanked catalog (score = 100/price × rating):\n");
        foreach (var row in rows.OrderByDescending(r => r.Score))
        {
            var p = row.Product;
            Console.WriteLine(
                $"  {row.Score,6:0.00}  {p.ProductId,-10}  {p.Title,-12}  ${p.Price,5:0.00}  ★{p.Rating:0.0}");
        }

        if (rows.Count == 0)
            Console.WriteLine("  (empty — all sources failed?)");
    }

    private sealed class Partial
    {
        public string? Title;
        public decimal? Price;
        public double? Rating;
    }
}

internal enum SourceKind
{
    Catalog,
    Pricing,
    Reviews
}

internal readonly record struct SourceFragment(
    string ProductId,
    SourceKind Kind,
    string? Title,
    decimal? Price,
    double? Rating);

internal readonly record struct EnrichedProduct(
    string ProductId,
    string Title,
    decimal Price,
    double Rating);

internal readonly record struct RankedProduct(EnrichedProduct Product, double Score);
