using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.Linq;
using DownKyi.Application.Downloads;
using DownKyi.Domain.Downloads;
using DownKyi.Models;

namespace DownKyi.Services.Migration;

internal static class LegacyDownloadTaskMapper
{
    public static Dictionary<string, bool> ReadRequestedAssets(ClassRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var values = record
            .GetArrayRecord("KeyValuePairs")?
            .GetArray(typeof(KeyValuePair<string, bool>[]));
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in values?.Cast<ClassRecord>() ?? [])
        {
            result[item.GetString("key") ?? string.Empty] = item.GetBoolean("value");
        }

        return result;
    }

    public static DownloadHistoryRecord RestoreHistory(Downloaded downloaded)
    {
        ArgumentNullException.ThrowIfNull(downloaded);
        var downloadBase = downloaded.DownloadBase
            ?? throw new ArgumentException("Legacy history is missing its base record.", nameof(downloaded));

        return new DownloadHistoryRecord(
            new DownloadTaskId(downloadBase.Id),
            downloadBase.Cid,
            downloadBase.ZoneId,
            downloadBase.Order,
            downloadBase.MainTitle,
            downloadBase.Name,
            downloadBase.Duration,
            downloadBase.VideoCodecName,
            downloadBase.Resolution.Id,
            downloadBase.Resolution.Name,
            downloadBase.AudioCodec.Name,
            downloadBase.FileSize,
            [],
            downloaded.FinishedTimestamp,
            downloaded.FinishedTime,
            downloaded.MaxSpeedDisplay);
    }
}
