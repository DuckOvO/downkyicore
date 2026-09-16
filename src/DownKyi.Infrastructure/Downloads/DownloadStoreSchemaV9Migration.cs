using Microsoft.Data.Sqlite;

namespace DownKyi.Infrastructure.Downloads;

internal static class DownloadStoreSchemaV9Migration
{
    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE download_history (
                id                    TEXT PRIMARY KEY,
                cid                   INTEGER NOT NULL DEFAULT 0,
                zone_id               INTEGER NOT NULL DEFAULT 0,
                [order]               INTEGER NOT NULL DEFAULT 0,
                main_title            TEXT NOT NULL DEFAULT '',
                name                  TEXT NOT NULL DEFAULT '',
                duration              TEXT NOT NULL DEFAULT '',
                video_codec_name      TEXT NOT NULL DEFAULT '',
                resolution            TEXT NOT NULL DEFAULT '{}',
                audio_codec           TEXT,
                file_size             TEXT,
                published_artifacts   TEXT NOT NULL DEFAULT '{}',
                finished_timestamp    INTEGER NOT NULL DEFAULT 0,
                finished_time         TEXT NOT NULL DEFAULT '',
                max_speed_display     TEXT
            );

            INSERT INTO download_history
                (id, cid, zone_id, [order], main_title, name, duration,
                 video_codec_name, resolution, audio_codec, file_size,
                 published_artifacts, finished_timestamp, finished_time,
                 max_speed_display)
            SELECT
                db.id, db.cid, db.zone_id, db.[order], db.main_title, db.name,
                db.duration, db.video_codec_name, db.resolution, db.audio_codec,
                db.file_size, db.published_artifacts, d.finished_timestamp,
                d.finished_time, d.max_speed_display
            FROM download_base db
            INNER JOIN downloaded d ON d.id = db.id;

            UPDATE download_quarantine
            SET source_table = 'download_history'
            WHERE source_table = 'downloaded'
              AND record_id IN (SELECT id FROM downloaded);

            DELETE FROM download_base
            WHERE id IN (SELECT id FROM downloaded);

            DROP TABLE downloaded;

            CREATE INDEX ix_download_history_finished_timestamp
                ON download_history(finished_timestamp DESC, id DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await DownloadStoreSchemaLifecycle
            .RecordMigrationAsync(connection, transaction, 9, appliedAtUtc, cancellationToken)
            .ConfigureAwait(false);
    }
}
