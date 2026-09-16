using System;
using DownKyi.Domain.Downloads;
using DownKyi.Models;

namespace DownKyi.Services.Migration;

internal static class LegacyDownloadHistoryTaskFactory
{
    public static DownloadTask Create(Downloaded downloaded, DateTimeOffset migratedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(downloaded);
        var downloadBase = downloaded.DownloadBase
            ?? throw new ArgumentException("Legacy history is missing its base record.", nameof(downloaded));
        var finishedAtUtc = downloaded.FinishedTimestamp > 0
            ? DateTimeOffset.FromUnixTimeSeconds(downloaded.FinishedTimestamp)
            : migratedAtUtc;
        var createdAtUtc = finishedAtUtc <= migratedAtUtc ? finishedAtUtc : migratedAtUtc;

        return DownloadTask.ImportCompletedHistory(
            new DownloadTaskId(downloadBase.Id),
            new DownloadTaskMetadata(
                new DownloadMediaIdentity(
                    downloadBase.Bvid,
                    downloadBase.Avid,
                    downloadBase.Cid,
                    downloadBase.EpisodeId,
                    downloadBase.Page,
                    downloadBase.Order),
                downloadBase.MainTitle,
                downloadBase.Name,
                downloadBase.Duration,
                downloadBase.VideoCodecName,
                new DownloadQuality(downloadBase.Resolution.Id, downloadBase.Resolution.Name),
                new DownloadQuality(downloadBase.AudioCodec.Id, downloadBase.AudioCodec.Name),
                downloadBase.CoverUrl,
                downloadBase.PageCoverUrl,
                downloadBase.ZoneId),
            new DownloadPlan(downloadBase.NeedDownloadContent, [], 0, nfoRequest: null),
            new DownloadOutput(downloadBase.FilePath, downloadBase.FileSize),
            new DownloadCompletion(
                downloaded.FinishedTimestamp,
                downloaded.FinishedTime,
                downloaded.MaxSpeedDisplay),
            createdAtUtc,
            migratedAtUtc);
    }
}
