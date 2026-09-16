using DownKyi.Domain.Downloads;
using DownKyi.Domain.Results;

namespace DownKyi.Application.Downloads;

public sealed class DownloadHistoryService : IDownloadHistoryService
{
    private readonly IDownloadTaskStore _tasks;
    private readonly IDownloadHistoryStore _history;

    public DownloadHistoryService(
        IDownloadTaskStore tasks,
        IDownloadHistoryStore history)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public static DownloadHistoryService CreateForSharedStore<TStore>(TStore store)
        where TStore : IDownloadTaskStore, IDownloadHistoryStore
    {
        ArgumentNullException.ThrowIfNull(store);
        return new DownloadHistoryService(store, store);
    }

    public Task<OperationResult> AddAsync(
        DownloadHistoryRecord history,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);
        return _history.AddHistoryAsync(history, cancellationToken);
    }

    public Task<OperationResult> CompleteAsync(
        DownloadTask completedTask,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completedTask);
        return _tasks.CompleteAsync(
            completedTask,
            DownloadHistoryRecord.FromCompletedTask(completedTask),
            expectedVersion,
            cancellationToken);
    }

    public Task<DownloadHistoryPage> GetPageAsync(
        DownloadHistoryCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken) =>
        _history.GetHistoryPageAsync(cursor, pageSize, cancellationToken);

    public Task<OperationResult> DeleteAsync(
        DownloadTaskId taskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        return _history.DeleteHistoryAsync(taskId, cancellationToken);
    }

    public Task<OperationResult> ClearAsync(CancellationToken cancellationToken) =>
        _history.ClearHistoryAsync(cancellationToken);
}
