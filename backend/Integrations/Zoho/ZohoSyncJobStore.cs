using System.Threading.Channels;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoSyncJobStore
{
    private readonly Channel<ZohoSyncJobWorkItem> queue = Channel.CreateUnbounded<ZohoSyncJobWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    internal bool TryEnqueue(ZohoSyncJobWorkItem workItem)
        => queue.Writer.TryWrite(workItem);

    internal IAsyncEnumerable<ZohoSyncJobWorkItem> ReadAllAsync(CancellationToken cancellationToken)
        => queue.Reader.ReadAllAsync(cancellationToken);
}
