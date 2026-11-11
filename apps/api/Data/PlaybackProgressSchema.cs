using Microsoft.EntityFrameworkCore;

namespace ChatGPTVoiceCompanion.Data;

public static class PlaybackProgressSchema
{
    public static void Ensure(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var close = connection.State != System.Data.ConnectionState.Open;
        if (close) connection.Open();
        try
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(CapturedMessages)";
                using var reader = command.ExecuteReader();
                while (reader.Read()) existing.Add(reader.GetString(1));
            }
            using var transaction = connection.BeginTransaction();
            foreach (var (name, type) in new[] {
                ("PlaybackPositionSeconds", "REAL"), ("PlaybackDurationSeconds", "REAL"),
                ("PlaybackProgressPercent", "INTEGER"), ("PlaybackCompleted", "INTEGER") })
            {
                if (existing.Contains(name)) continue;
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"ALTER TABLE CapturedMessages ADD COLUMN {name} {type} NOT NULL DEFAULT 0";
                command.ExecuteNonQuery();
            }
            if (!existing.Contains("PlaybackCompleted"))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE CapturedMessages SET PlaybackCompleted = 1, PlaybackProgressPercent = 100 WHERE SpokeAt IS NOT NULL";
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        finally { if (close) connection.Close(); }
    }
}
