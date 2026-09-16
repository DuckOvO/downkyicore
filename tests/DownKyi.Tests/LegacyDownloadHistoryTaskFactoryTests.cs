using DownKyi.Core.BiliApi.BiliUtils;
using DownKyi.Domain.Downloads;
using DownKyi.Models;
using DownKyi.Services.Migration;

namespace DownKyi.Tests;

public sealed class LegacyDownloadHistoryTaskFactoryTests
{
    [Fact]
    public void CreatePreservesLegacyHistoryMeaning()
    {
        const long finishedTimestamp = 1_700_000_000;
        var migratedAtUtc = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        var requestedContent = new DownloadContentSelection(false, true, false, true, false);
        var downloaded = new Downloaded
        {
            Id = "legacy-history",
            FinishedTimestamp = finishedTimestamp,
            FinishedTime = "finished",
            MaxSpeedDisplay = "25 MB/s",
            DownloadBase = new DownloadBase
            {
                Id = "legacy-history",
                NeedDownloadContent = requestedContent,
                Bvid = "BV1LEGACY",
                Avid = 1,
                Cid = 2,
                EpisodeId = 3,
                Page = 4,
                Order = 5,
                MainTitle = "Series",
                Name = "Episode",
                Duration = "01:00",
                VideoCodecName = "AVC",
                Resolution = new Quality { Id = 80, Name = "1080P" },
                AudioCodec = new Quality { Id = 30280, Name = "192K" },
                CoverUrl = "cover",
                PageCoverUrl = "page-cover",
                ZoneId = 6,
                FilePath = "legacy-output",
                FileSize = "1 GB"
            }
        };

        var history = LegacyDownloadHistoryTaskFactory.Create(downloaded, migratedAtUtc);

        Assert.Equal("legacy-history", history.Id.Value);
        Assert.Equal(DownloadPhase.Completed, history.Phase);
        Assert.Equal(requestedContent, history.Plan.RequestedContent);
        Assert.Equal(finishedTimestamp, history.Completion?.FinishedTimestamp);
        Assert.Equal("finished", history.Completion?.FinishedTimeText);
        Assert.Equal("25 MB/s", history.Completion?.MaximumSpeedText);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(finishedTimestamp), history.CreatedAtUtc);
        Assert.Equal(migratedAtUtc, history.UpdatedAtUtc);
        Assert.Equal(0, history.Version);
    }
}
