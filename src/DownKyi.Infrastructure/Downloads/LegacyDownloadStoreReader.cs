using Microsoft.Data.Sqlite;

namespace DownKyi.Infrastructure.Downloads;

internal static class LegacyDownloadStoreReader
{
    public static async Task<LegacyDownloadStoreSnapshot> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LegacyDownloadStoreFormat format,
        CancellationToken cancellationToken)
    {
        if (!format.IsSupported)
        {
            throw new SqliteException(
                $"Download database schema {format.UserVersion} has an unsupported or incomplete shape.",
                1);
        }

        var downloadRows = await ReadDownloadRowsAsync(
            connection,
            transaction,
            format.HasReservationKey,
            cancellationToken).ConfigureAwait(false);
        var baseRecordIds = format.HasStagingToken
            ? []
            : await ReadBaseRecordIdsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        return new LegacyDownloadStoreSnapshot(format, downloadRows, baseRecordIds);
    }

    private static async Task<IReadOnlyList<LegacyDownloadRow>> ReadDownloadRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        bool hasReservationKey,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (hasReservationKey)
        {
            command.CommandText = """
                SELECT db.id, db.file_path, dl.download_status, db.output_reservation_key,
                       EXISTS (SELECT 1 FROM downloaded d WHERE d.id = db.id)
                FROM download_base db
                INNER JOIN downloading dl ON dl.id = db.id
                ORDER BY db.id
                """;
        }
        else
        {
            command.CommandText = """
                SELECT db.id, db.file_path, dl.download_status, NULL,
                       EXISTS (SELECT 1 FROM downloaded d WHERE d.id = db.id)
                FROM download_base db
                INNER JOIN downloading dl ON dl.id = db.id
                ORDER BY db.id
                """;
        }

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<LegacyDownloadRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new LegacyDownloadRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                    ? null : reader.GetString(3),
                reader.GetInt32(4) != 0));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<string>> ReadBaseRecordIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM download_base ORDER BY id";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }
}
