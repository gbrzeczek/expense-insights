using System.Threading.Channels;

namespace ExpenseInsights.Infrastructure.Classification;

/// <summary>
/// Users whose transactions need (re)classifying, for the background worker. A user queued twice
/// before the worker gets to them is classified once.
/// </summary>
public sealed class ClassificationRequests
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<Guid> _queued = [];

    public void Enqueue(Guid userId)
    {
        lock (_queued)
        {
            if (!_queued.Add(userId))
            {
                return;
            }
        }

        _channel.Writer.TryWrite(userId);
    }

    public async IAsyncEnumerable<Guid> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var userId in _channel.Reader.ReadAllAsync(ct))
        {
            lock (_queued)
            {
                _queued.Remove(userId);
            }

            yield return userId;
        }
    }
}
