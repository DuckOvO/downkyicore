using Microsoft.Data.Sqlite;

namespace DownKyi.Infrastructure.Downloads;

internal static class LegacyDownloadStoreFormatDetector
{
    private static readonly string[] CoreBaseColumns =
    [
        "id",
        "need_download_content",
        "bvid",
        "avid",
        "cid",
        "episode_id",
        "cover_url",
        "page_cover_url",
        "zone_id",
        "order",
        "main_title",
        "name",
        "duration",
        "video_codec_name",
        "resolution",
        "audio_codec",
        "file_path",
        "file_size",
        "page"
    ];

    private static readonly string[] CoreDownloadingColumns =
    [
        "id",
        "gid",
        "download_files",
        "downloaded_files",
        "play_stream_type",
        "download_status",
        "download_content",
        "download_status_title",
        "progress",
        "downloading_file_size",
        "max_speed",
        "speed_display"
    ];

    private static readonly string[] CoreDownloadedColumns =
    [
        "id",
        "max_speed_display",
        "finished_timestamp",
        "finished_time"
    ];

    private static readonly string[] CurrentHistoryColumns =
    [
        "id",
        "cid",
        "zone_id",
        "order",
        "main_title",
        "name",
        "duration",
        "video_codec_name",
        "resolution",
        "audio_codec",
        "file_size",
        "published_artifacts",
        "finished_timestamp",
        "finished_time",
        "max_speed_display"
    ];

    private static readonly string[] BaseStateColumns =
    [
        "version",
        "created_at_utc",
        "updated_at_utc"
    ];

    private static readonly string[] DownloadingStateColumns =
    [
        "phase",
        "failure_code",
        "failure_message",
        "failure_transient",
        "downloaded_bytes",
        "total_bytes",
        "bytes_per_second"
    ];

    private static readonly string[] PublishingColumns =
    [
        "publishing_key",
        "publishing_file_name",
        "publishing_length",
        "publishing_sha256"
    ];

    public static async Task<LegacyDownloadStoreFormat> DetectAsync(
        SqliteConnection connection,
        bool databaseExisted,
        CancellationToken cancellationToken)
    {
        if (!databaseExisted)
        {
            return new LegacyDownloadStoreFormat(
                Kind: LegacyDownloadStoreKind.New,
                DatabaseExisted: false,
                UserVersion: 0,
                HasCoreTables: false,
                HasSchemaLedger: false,
                HasQuarantine: false,
                HasStateColumns: false,
                HasReservationKey: false,
                HasAdmissionGate: false,
                HasNfoRequest: false,
                HasPublishedArtifacts: false,
                HasStagingToken: false,
                HasPublishingArtifact: false);
        }

        var userVersion = await DownloadStoreSchemaLifecycle
            .ReadUserVersionAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        if (userVersion > DownloadStoreSchema.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Download database schema {userVersion} is newer than supported schema {DownloadStoreSchema.CurrentVersion}.");
        }

        var tables = await ReadTableNamesAsync(connection, cancellationToken).ConfigureAwait(false);
        var baseColumns = tables.Contains("download_base")
            ? await ReadDownloadBaseColumnsAsync(connection, cancellationToken).ConfigureAwait(false)
            : [];
        var downloadingColumns = tables.Contains("downloading")
            ? await ReadDownloadingColumnsAsync(connection, cancellationToken).ConfigureAwait(false)
            : [];
        var downloadedColumns = tables.Contains("downloaded")
            ? await ReadDownloadedColumnsAsync(connection, cancellationToken).ConfigureAwait(false)
            : [];
        var historyColumns = tables.Contains("download_history")
            ? await ReadHistoryColumnsAsync(connection, cancellationToken).ConfigureAwait(false)
            : [];
        var hasLegacyCoreTables = CoreBaseColumns.All(baseColumns.Contains)
                                  && CoreDownloadingColumns.All(downloadingColumns.Contains)
                                  && CoreDownloadedColumns.All(downloadedColumns.Contains);
        var hasCurrentHistoryShape = CoreBaseColumns.All(baseColumns.Contains)
                                     && CoreDownloadingColumns.All(downloadingColumns.Contains)
                                     && CurrentHistoryColumns.All(historyColumns.Contains)
                                     && !tables.Contains("downloaded");
        var hasStateColumns = BaseStateColumns.All(baseColumns.Contains)
                              && DownloadingStateColumns.All(downloadingColumns.Contains);
        var hasAnyStateColumns = BaseStateColumns.Any(baseColumns.Contains)
                                 || DownloadingStateColumns.Any(downloadingColumns.Contains);
        var hasReservationKey = baseColumns.Contains("output_reservation_key");
        var hasAdmissionGate = tables.Contains("download_upgrade_admission_gate");
        var hasNfoRequest = baseColumns.Contains("nfo_request");
        var hasPublishedArtifacts = baseColumns.Contains("published_artifacts");
        var hasStagingToken = baseColumns.Contains("staging_token");
        var hasPublishingArtifact = PublishingColumns.All(baseColumns.Contains);
        var hasAnyPublishingArtifact = PublishingColumns.Any(baseColumns.Contains);
        var kind = DetectKind(
            userVersion,
            tables.Count == 0,
            hasLegacyCoreTables,
            tables.Contains("download_history"),
            hasCurrentHistoryShape,
            tables.Contains("download_schema_migrations"),
            tables.Contains("download_quarantine"),
            hasStateColumns,
            hasAnyStateColumns,
            hasReservationKey,
            hasAdmissionGate,
            hasNfoRequest,
            hasPublishedArtifacts,
            hasStagingToken,
            hasPublishingArtifact,
            hasAnyPublishingArtifact);

        return new LegacyDownloadStoreFormat(
            Kind: kind,
            DatabaseExisted: true,
            UserVersion: userVersion,
            HasCoreTables: hasLegacyCoreTables || hasCurrentHistoryShape,
            HasSchemaLedger: tables.Contains("download_schema_migrations"),
            HasQuarantine: tables.Contains("download_quarantine"),
            HasStateColumns: hasStateColumns,
            HasReservationKey: hasReservationKey,
            HasAdmissionGate: hasAdmissionGate,
            HasNfoRequest: hasNfoRequest,
            HasPublishedArtifacts: hasPublishedArtifacts,
            HasStagingToken: hasStagingToken,
            HasPublishingArtifact: hasPublishingArtifact);
    }

    private static LegacyDownloadStoreKind DetectKind(
        int userVersion,
        bool hasNoTables,
        bool hasLegacyCoreTables,
        bool hasHistoryTable,
        bool hasCurrentHistoryShape,
        bool hasSchemaLedger,
        bool hasQuarantine,
        bool hasStateColumns,
        bool hasAnyStateColumns,
        bool hasReservationKey,
        bool hasAdmissionGate,
        bool hasNfoRequest,
        bool hasPublishedArtifacts,
        bool hasStagingToken,
        bool hasPublishingArtifact,
        bool hasAnyPublishingArtifact)
    {
        if (userVersion == 0 && hasNoTables)
        {
            return LegacyDownloadStoreKind.New;
        }

        if (hasCurrentHistoryShape
            && userVersion == DownloadStoreSchema.CurrentVersion
            && hasSchemaLedger
            && hasQuarantine
            && hasStateColumns
            && hasReservationKey
            && hasAdmissionGate
            && hasNfoRequest
            && hasPublishedArtifacts
            && hasStagingToken
            && hasPublishingArtifact)
        {
            return LegacyDownloadStoreKind.Current;
        }

        if (hasHistoryTable
            || !hasLegacyCoreTables
            || hasAnyStateColumns != hasStateColumns
            || hasAnyPublishingArtifact != hasPublishingArtifact)
        {
            return LegacyDownloadStoreKind.Unsupported;
        }

        if (!hasStateColumns)
        {
            var hasOnlyRelationalColumns = !hasReservationKey
                                           && !hasAdmissionGate
                                           && !hasNfoRequest
                                           && !hasPublishedArtifacts
                                           && !hasStagingToken
                                           && !hasPublishingArtifact;
            var isKnownV0 = userVersion == 0 && !hasSchemaLedger && !hasQuarantine;
            var isKnownV1 = userVersion == 1 && hasSchemaLedger && hasQuarantine;
            return hasOnlyRelationalColumns && (isKnownV0 || isKnownV1)
                ? LegacyDownloadStoreKind.Relational
                : LegacyDownloadStoreKind.Unsupported;
        }

        if (!hasSchemaLedger || !hasQuarantine)
        {
            return LegacyDownloadStoreKind.Unsupported;
        }

        if (!hasReservationKey)
        {
            return userVersion == 2
                   && !hasAdmissionGate
                   && !hasNfoRequest
                   && !hasPublishedArtifacts
                   && !hasStagingToken
                   && !hasPublishingArtifact
                ? LegacyDownloadStoreKind.Stateful
                : LegacyDownloadStoreKind.Unsupported;
        }

        if (!hasAdmissionGate)
        {
            return userVersion == 3
                   && !hasNfoRequest
                   && !hasPublishedArtifacts
                   && !hasStagingToken
                   && !hasPublishingArtifact
                ? LegacyDownloadStoreKind.Reserved
                : LegacyDownloadStoreKind.Unsupported;
        }

        var additiveShapeVersion = hasPublishingArtifact
            ? 8
            : hasStagingToken
                ? 7
                : hasPublishedArtifacts
                    ? 6
                    : hasNfoRequest
                        ? 5
                        : 4;
        var hasMonotonicAdditiveShape = (!hasPublishedArtifacts || hasNfoRequest)
                                        && (!hasStagingToken || hasPublishedArtifacts)
                                        && (!hasPublishingArtifact || hasStagingToken);
        if (!hasMonotonicAdditiveShape || userVersion != additiveShapeVersion)
        {
            return LegacyDownloadStoreKind.Unsupported;
        }

        return LegacyDownloadStoreKind.AdmissionSafe;
    }

    private static async Task<HashSet<string>> ReadDownloadBaseColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(download_base)";
        return await ReadColumnNamesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> ReadDownloadingColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(downloading)";
        return await ReadColumnNamesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> ReadDownloadedColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(downloaded)";
        return await ReadColumnNamesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> ReadHistoryColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(download_history)";
        return await ReadColumnNamesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
