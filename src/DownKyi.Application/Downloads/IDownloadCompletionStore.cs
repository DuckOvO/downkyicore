using DownKyi.Domain.Downloads;
using DownKyi.Domain.Results;

namespace DownKyi.Application.Downloads;

public interface IDownloadCompletionStore
{
    Task<OperationResult> CompleteAsync(
        DownloadTask task,
        DownloadHistoryRecord history,
        long expectedVersion,
        CancellationToken cancellationToken);
}
