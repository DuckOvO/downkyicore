using DownKyi.Application.Downloads;
using DownKyi.Application.Time;
using DownKyi.Domain.Downloads;
using Microsoft.Data.Sqlite;

namespace DownKyi.Infrastructure.Downloads;

internal sealed class SqliteDownloadStoreQueries(
    SqliteDownloadStoreDatabase database,
    IClock clock)
{
    private const int MaximumHistoryPageSize = 500;
    private readonly SqliteDownloadStoreDatabase _database = database;
    private readonly IClock _clock = clock;

    public async Task<DownloadTask?> FindAsync(
        DownloadTaskId taskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = DownloadTaskSqlReader.SelectColumns + "\n" + """
            WHERE db.id = @id
              AND NOT EXISTS (
                  SELECT 1 FROM download_quarantine q
                  WHERE q.record_id = db.id
                    AND q.source_table = 'downloading')
            """;
        command.Parameters.AddWithValue("@id", taskId.Value);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return DownloadTaskRecordMapper.Read(reader);
        }
        catch (DownloadRecordCorruptException exception)
        {
            await reader.DisposeAsync().ConfigureAwait(false);
            await SqliteDownloadStoreQuarantine
                .RecordAsync(
                    connection,
                    "downloading",
                    taskId.Value,
                    exception,
                    _clock.UtcNow,
                    cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
    }

    public async Task<IReadOnlyList<DownloadTask>> GetUnfinishedAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = DownloadTaskSqlReader.SelectColumns + "\n" + """
            WHERE NOT EXISTS (
                  SELECT 1 FROM download_quarantine q
                  WHERE q.source_table = 'downloading' AND q.record_id = db.id)
            ORDER BY db.main_title COLLATE NOCASE, db.[order] ASC, db.id ASC
            """;
        return await ReadManyAsync(
            connection,
            command,
            "downloading",
            _clock.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DownloadHistoryPage> GetHistoryPageAsync(
        DownloadHistoryCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaximumHistoryPageSize);
        using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = DownloadHistoryRecordMapper.SelectColumns + "\n" + """
            WHERE (@cursor_timestamp IS NULL
                   OR h.finished_timestamp < @cursor_timestamp
                   OR (h.finished_timestamp = @cursor_timestamp AND h.id < @cursor_id))
              AND NOT EXISTS (
                  SELECT 1 FROM download_quarantine q
                  WHERE q.source_table = 'download_history' AND q.record_id = h.id)
            ORDER BY h.finished_timestamp DESC, h.id DESC
            LIMIT @limit
            """;
        var cursorTimestamp = command.Parameters.Add("@cursor_timestamp", SqliteType.Integer);
        var cursorId = command.Parameters.Add("@cursor_id", SqliteType.Text);
        var limit = command.Parameters.Add("@limit", SqliteType.Integer);
        var targetCount = checked(pageSize + 1);
        var items = new List<DownloadHistoryRecord>(targetCount);
        var scanCursor = cursor;
        while (items.Count < targetCount)
        {
            cursorTimestamp.Value = scanCursor == null
                ? DBNull.Value
                : scanCursor.FinishedTimestamp;
            cursorId.Value = scanCursor?.TaskId.Value ?? string.Empty;
            var remainingCount = targetCount - items.Count;
            limit.Value = remainingCount;
            var batch = await ReadHistoryManyAsync(
                    connection,
                    command,
                    _clock.UtcNow,
                    cancellationToken)
                .ConfigureAwait(false);
            items.AddRange(batch.Records);
            if (batch.RowsRead < remainingCount)
            {
                break;
            }

            if (batch.Records.Count > 0)
            {
                var last = batch.Records[^1];
                scanCursor = new DownloadHistoryCursor(last.FinishedTimestamp, last.Id);
            }
        }

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        DownloadHistoryCursor? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = new DownloadHistoryCursor(last.FinishedTimestamp, last.Id);
        }

        return new DownloadHistoryPage(items, nextCursor);
    }

    private static async Task<(IReadOnlyList<DownloadHistoryRecord> Records, int RowsRead)>
        ReadHistoryManyAsync(
        SqliteConnection connection,
        SqliteCommand command,
        DateTimeOffset quarantinedAtUtc,
        CancellationToken cancellationToken)
    {
        var records = new List<DownloadHistoryRecord>();
        var corrupt = new List<(string RecordId, DownloadRecordCorruptException Error)>();
        var rowsRead = 0;
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rowsRead++;
                var recordId = reader.GetString(reader.GetOrdinal("id"));
                try
                {
                    records.Add(DownloadHistoryRecordMapper.Read(reader));
                }
                catch (DownloadRecordCorruptException exception)
                {
                    corrupt.Add((recordId, exception));
                }
            }
        }

        foreach (var item in corrupt)
        {
            await SqliteDownloadStoreQuarantine
                .RecordAsync(
                    connection,
                    "download_history",
                    item.RecordId,
                    item.Error,
                    quarantinedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return (records, rowsRead);
    }

    private static async Task<IReadOnlyList<DownloadTask>> ReadManyAsync(
        SqliteConnection connection,
        SqliteCommand command,
        string sourceTable,
        DateTimeOffset quarantinedAtUtc,
        CancellationToken cancellationToken)
    {
        var tasks = new List<DownloadTask>();
        var corrupt = new List<(string RecordId, DownloadRecordCorruptException Error)>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var recordId = reader.GetString(reader.GetOrdinal("id"));
                try
                {
                    tasks.Add(DownloadTaskRecordMapper.Read(reader));
                }
                catch (DownloadRecordCorruptException exception)
                {
                    corrupt.Add((recordId, exception));
                }
            }
        }

        foreach (var item in corrupt)
        {
            await SqliteDownloadStoreQuarantine
                .RecordAsync(
                    connection,
                    sourceTable,
                    item.RecordId,
                    item.Error,
                    quarantinedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return tasks;
    }
}
