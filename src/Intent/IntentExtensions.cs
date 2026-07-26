using System.Threading.Channels;

namespace Intents;

public static class IntentExtensions
{
    /// <summary>
    /// Schedules the intent without awaiting. Faults go to <see cref="IntentDiagnostics"/>.
    /// </summary>
    public static void Background(this Intent intent) => Intent.Background(intent);

    /// <summary>
    /// Runs the intent in the background and writes the result to <paramref name="writer"/>.
    /// Faults go to <see cref="IntentDiagnostics"/>; the channel is not completed.
    /// </summary>
    public static void Into<T>(this Intent<T> intent, ChannelWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(writer);
        _ = RunIntoAsync(intent, writer);
    }

    /// <summary>
    /// Runs the intent in the background and writes the result to <paramref name="channel"/>.
    /// Faults go to <see cref="IntentDiagnostics"/>; the channel is not completed.
    /// </summary>
    public static void Into<T>(this Intent<T> intent, Channel<T> channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        intent.Into(channel.Writer);
    }

    /// <summary>
    /// Background consumer: for each item read from <paramref name="reader"/>, builds and awaits an Intent.
    /// Item faults go to <see cref="IntentDiagnostics"/>; the loop continues. Opposite of <see cref="Into{T}(Intent{T}, ChannelWriter{T})"/>.
    /// </summary>
    public static void FromEach<T>(
        this ChannelReader<T> reader,
        Func<T, Intent> body,
        CancellationToken cancellationToken,
        Action? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(body);
        _ = RunFromEachAsync(reader, body, onCompleted, cancellationToken);
    }

    /// <summary>
    /// Background consumer over <paramref name="channel"/>.Reader — see
    /// <see cref="FromEach{T}(ChannelReader{T}, Func{T, Intent}, CancellationToken, Action?)"/>.
    /// </summary>
    public static void FromEach<T>(
        this Channel<T> channel,
        Func<T, Intent> body,
        CancellationToken cancellationToken,
        Action? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        channel.Reader.FromEach(body, cancellationToken, onCompleted);
    }

    /// <summary>
    /// Background consumer: for each item, awaits <paramref name="body"/> and optionally writes the result to
    /// <paramref name="into"/>. When the reader completes, <paramref name="into"/> is completed if
    /// <paramref name="completeWriter"/> is true.
    /// </summary>
    public static void FromEach<T, TResult>(
        this ChannelReader<T> reader,
        Func<T, Intent<TResult>> body,
        CancellationToken cancellationToken,
        ChannelWriter<TResult>? into = null,
        bool completeWriter = true,
        Action? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(body);
        _ = RunFromEachAsync(reader, body, into, completeWriter, onCompleted, cancellationToken);
    }

    /// <summary>
    /// Background consumer over <paramref name="channel"/>.Reader — see
    /// <see cref="FromEach{T, TResult}(ChannelReader{T}, Func{T, Intent{TResult}}, CancellationToken, ChannelWriter{TResult}?, bool, Action?)"/>.
    /// </summary>
    public static void FromEach<T, TResult>(
        this Channel<T> channel,
        Func<T, Intent<TResult>> body,
        CancellationToken cancellationToken,
        ChannelWriter<TResult>? into = null,
        bool completeWriter = true,
        Action? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        channel.Reader.FromEach(body, cancellationToken, into, completeWriter, onCompleted);
    }

    private static async Task RunIntoAsync<T>(Intent<T> intent, ChannelWriter<T> writer)
    {
        try
        {
            var result = await intent;
            await writer.WriteAsync(result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            EmitFault(ex);
        }
    }

    private static async Task RunFromEachAsync<T>(
        ChannelReader<T> reader,
        Func<T, Intent> body,
        Action? onCompleted,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await body(item);
                }
                catch (Exception ex)
                {
                    EmitFault(ex);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // swallow cooperative cancel of the consumer
        }
        catch (Exception ex)
        {
            EmitFault(ex);
        }
        finally
        {
            onCompleted?.Invoke();
        }
    }

    private static async Task RunFromEachAsync<T, TResult>(
        ChannelReader<T> reader,
        Func<T, Intent<TResult>> body,
        ChannelWriter<TResult>? into,
        bool completeWriter,
        Action? onCompleted,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var result = await body(item);
                    if (into is not null)
                        await into.WriteAsync(result, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    EmitFault(ex);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            EmitFault(ex);
        }
        finally
        {
            if (completeWriter)
                into?.TryComplete();
            onCompleted?.Invoke();
        }
    }

    private static void EmitFault(Exception ex) =>
        IntentDiagnostics.EmitTrace(new IntentTraceEvent(
            IntentAmbient.Name, IntentTracePhase.Faulted, null, ex));
}
