namespace DownKyi.Domain.Downloads;

public sealed partial class DownloadTask
{
    public static DownloadTask ImportCompletedHistory(
        DownloadTaskId id,
        DownloadTaskMetadata metadata,
        DownloadPlan plan,
        DownloadOutput output,
        DownloadCompletion completion,
        DateTimeOffset createdAtUtc,
        DateTimeOffset migratedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return Restore(
            id,
            metadata,
            plan,
            output,
            DownloadPhase.Completed,
            DownloadProgress.None,
            DownloadTransferState.Empty,
            failure: null,
            completion,
            version: 0,
            createdAtUtc,
            migratedAtUtc);
    }
}
