using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StickerMemo.Models;

namespace StickerMemo.Services
{
    public class NoteStorageService
    {
        private static readonly string DbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StickerMemo"
        );
        private static readonly string DbPath = Path.Combine(DbFolder, "AppNotes.db");
        private static readonly string ConnectionString = $"Data Source={DbPath};Mode=ReadWriteCreate;Cache=Shared";
        // Every NoteStorageService instance targets the same database. Keep writes
        // serialized process-wide even if a caller creates another service instance.
        private static readonly SemaphoreSlim WriteLock = new(1, 1);

        public NoteStorageService()
        {
            EnsureDatabaseCreated();
        }

        public static void ClearConnectionPools()
        {
            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch { }
        }

        private void EnsureDatabaseCreated()
        {
            App.LogStartup($"NoteStorageService: Initializing SQLite at '{DbPath}'...");
            if (!Directory.Exists(DbFolder))
            {
                Directory.CreateDirectory(DbFolder);
            }

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            App.LogStartup("NoteStorageService: SQLite connection opened successfully.");

            var sql = @"
                CREATE TABLE IF NOT EXISTS Notes (
                    Id TEXT PRIMARY KEY,
                    RemoteId TEXT,
                    Title TEXT,
                    Text TEXT,
                    X REAL,
                    Y REAL,
                    Width REAL,
                    Height REAL,
                    ExpandedHeight REAL,
                    IsCollapsed INTEGER,
                    IsArchived INTEGER DEFAULT 0,
                    IsFloating INTEGER DEFAULT 0,
                    SortOrder INTEGER DEFAULT 0,
                    ThemeId TEXT,
                    IsTopmost INTEGER,
                    TapeAngle REAL,
                    FontFamily TEXT DEFAULT 'Malgun Gothic',
                    FontSize REAL DEFAULT 13.5,
                    IsBold INTEGER DEFAULT 0,
                    CreatedAt TEXT,
                    UpdatedAt TEXT
                );";

            using (var cmd = new SqliteCommand(sql, connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Auto-migrate schema for new columns if table previously existed
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var pragmaCmd = new SqliteCommand("PRAGMA table_info(Notes);", connection))
            using (var reader = pragmaCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    columns.Add(reader.GetString(1));
                }
            }

            if (!columns.Contains("RemoteId"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN RemoteId TEXT;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("Title"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN Title TEXT;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("IsArchived"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN IsArchived INTEGER DEFAULT 0;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("IsFloating"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN IsFloating INTEGER DEFAULT 0;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("SortOrder"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN SortOrder INTEGER DEFAULT 0;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("ExpandedHeight"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN ExpandedHeight REAL DEFAULT 340;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("IsCollapsed"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN IsCollapsed INTEGER DEFAULT 0;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("FontFamily"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN FontFamily TEXT DEFAULT 'Malgun Gothic';", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("FontSize"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN FontSize REAL DEFAULT 13.5;", connection);
                alterCmd.ExecuteNonQuery();
            }
            if (!columns.Contains("IsBold"))
            {
                using var alterCmd = new SqliteCommand("ALTER TABLE Notes ADD COLUMN IsBold INTEGER DEFAULT 0;", connection);
                alterCmd.ExecuteNonQuery();
            }
        }

        public List<NoteModel> GetAllActiveNotes()
        {
            var list = new List<NoteModel>();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var sql = "SELECT Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight, IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost, TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt FROM Notes WHERE IsArchived = 0 ORDER BY SortOrder ASC, UpdatedAt DESC";
            using var cmd = new SqliteCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var note = ReadNoteFromReader(reader);
                note.RecomputeCachedPreviews();
                list.Add(note);
            }

            return list;
        }

        public List<NoteModel> GetAllArchivedNotes()
        {
            var list = new List<NoteModel>();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var sql = "SELECT Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight, IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost, TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt FROM Notes WHERE IsArchived = 1 ORDER BY UpdatedAt DESC";
            using var cmd = new SqliteCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var note = ReadNoteFromReader(reader);
                note.RecomputeCachedPreviews();
                list.Add(note);
            }

            return list;
        }

        private static NoteModel ReadNoteFromReader(SqliteDataReader reader)
        {
            return new NoteModel
            {
                Id = reader.GetString(0),
                RemoteId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Title = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Text = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                X = reader.IsDBNull(4) ? double.NaN : reader.GetDouble(4),
                Y = reader.IsDBNull(5) ? double.NaN : reader.GetDouble(5),
                Width = reader.IsDBNull(6) ? 340 : reader.GetDouble(6),
                Height = reader.IsDBNull(7) ? 340 : reader.GetDouble(7),
                ExpandedHeight = reader.IsDBNull(8) ? 340 : reader.GetDouble(8),
                IsCollapsed = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                IsArchived = !reader.IsDBNull(10) && reader.GetInt32(10) == 1,
                IsFloating = !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                SortOrder = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                ThemeId = reader.IsDBNull(13) ? "Yellow" : reader.GetString(13),
                IsTopmost = !reader.IsDBNull(14) && reader.GetInt32(14) == 1,
                TapeAngle = reader.IsDBNull(15) ? -0.5 : reader.GetDouble(15),
                FontFamily = reader.IsDBNull(16) ? "Malgun Gothic" : reader.GetString(16),
                FontSize = reader.IsDBNull(17) ? 13.5 : reader.GetDouble(17),
                IsBold = !reader.IsDBNull(18) && reader.GetInt32(18) == 1,
                CreatedAt = reader.IsDBNull(19) ? DateTime.Now : DateTime.Parse(reader.GetString(19)),
                UpdatedAt = reader.IsDBNull(20) ? DateTime.Now : DateTime.Parse(reader.GetString(20)),
            };
        }

        public HashSet<string> GetExistingRemoteIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT RemoteId FROM Notes WHERE RemoteId IS NOT NULL;", connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    ids.Add(reader.GetString(0));
                }
            }
            return ids;
        }

        public async Task SaveOrUpdateNoteAsync(NoteModel note)
        {
            await WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync().ConfigureAwait(false);

                var sql = @"
                    INSERT INTO Notes (Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight, IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost, TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt)
                    VALUES (@Id, @RemoteId, @Title, @Text, @X, @Y, @Width, @Height, @ExpandedHeight, @IsCollapsed, @IsArchived, @IsFloating, @SortOrder, @ThemeId, @IsTopmost, @TapeAngle, @FontFamily, @FontSize, @IsBold, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        RemoteId = excluded.RemoteId,
                        Title = excluded.Title,
                        Text = excluded.Text,
                        X = excluded.X,
                        Y = excluded.Y,
                        Width = excluded.Width,
                        Height = excluded.Height,
                        ExpandedHeight = excluded.ExpandedHeight,
                        IsCollapsed = excluded.IsCollapsed,
                        IsArchived = excluded.IsArchived,
                        IsFloating = excluded.IsFloating,
                        SortOrder = excluded.SortOrder,
                        ThemeId = excluded.ThemeId,
                        IsTopmost = excluded.IsTopmost,
                        TapeAngle = excluded.TapeAngle,
                        FontFamily = excluded.FontFamily,
                        FontSize = excluded.FontSize,
                        IsBold = excluded.IsBold,
                        UpdatedAt = excluded.UpdatedAt;";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", note.Id);
                cmd.Parameters.AddWithValue("@RemoteId", string.IsNullOrEmpty(note.RemoteId) ? DBNull.Value : note.RemoteId);
                cmd.Parameters.AddWithValue("@Title", note.Title ?? string.Empty);
                cmd.Parameters.AddWithValue("@Text", note.Text ?? string.Empty);
                cmd.Parameters.AddWithValue("@X", double.IsNaN(note.X) ? DBNull.Value : note.X);
                cmd.Parameters.AddWithValue("@Y", double.IsNaN(note.Y) ? DBNull.Value : note.Y);
                cmd.Parameters.AddWithValue("@Width", note.Width);
                cmd.Parameters.AddWithValue("@Height", note.Height);
                cmd.Parameters.AddWithValue("@ExpandedHeight", note.ExpandedHeight > 0 ? note.ExpandedHeight : 340);
                cmd.Parameters.AddWithValue("@IsCollapsed", note.IsCollapsed ? 1 : 0);
                cmd.Parameters.AddWithValue("@IsArchived", note.IsArchived ? 1 : 0);
                cmd.Parameters.AddWithValue("@IsFloating", note.IsFloating ? 1 : 0);
                cmd.Parameters.AddWithValue("@SortOrder", note.SortOrder);
                cmd.Parameters.AddWithValue("@ThemeId", note.ThemeId ?? "Yellow");
                cmd.Parameters.AddWithValue("@IsTopmost", note.IsTopmost ? 1 : 0);
                cmd.Parameters.AddWithValue("@TapeAngle", note.TapeAngle);
                cmd.Parameters.AddWithValue("@FontFamily", note.FontFamily ?? "Malgun Gothic");
                cmd.Parameters.AddWithValue("@FontSize", note.FontSize);
                cmd.Parameters.AddWithValue("@IsBold", note.IsBold ? 1 : 0);
                cmd.Parameters.AddWithValue("@CreatedAt", note.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));

                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        public async Task SaveOrUpdateNotesBatchAsync(IEnumerable<NoteModel> notes)
        {
            await WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync().ConfigureAwait(false);
                using var transaction = connection.BeginTransaction();

                var sql = @"
                    INSERT INTO Notes (Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight, IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost, TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt)
                    VALUES (@Id, @RemoteId, @Title, @Text, @X, @Y, @Width, @Height, @ExpandedHeight, @IsCollapsed, @IsArchived, @IsFloating, @SortOrder, @ThemeId, @IsTopmost, @TapeAngle, @FontFamily, @FontSize, @IsBold, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        RemoteId = excluded.RemoteId,
                        Title = excluded.Title,
                        Text = excluded.Text,
                        X = excluded.X,
                        Y = excluded.Y,
                        Width = excluded.Width,
                        Height = excluded.Height,
                        ExpandedHeight = excluded.ExpandedHeight,
                        IsCollapsed = excluded.IsCollapsed,
                        IsArchived = excluded.IsArchived,
                        IsFloating = excluded.IsFloating,
                        SortOrder = excluded.SortOrder,
                        ThemeId = excluded.ThemeId,
                        IsTopmost = excluded.IsTopmost,
                        TapeAngle = excluded.TapeAngle,
                        FontFamily = excluded.FontFamily,
                        FontSize = excluded.FontSize,
                        IsBold = excluded.IsBold,
                        UpdatedAt = excluded.UpdatedAt;";

                foreach (var note in notes)
                {
                    using var cmd = new SqliteCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@Id", note.Id);
                    cmd.Parameters.AddWithValue("@RemoteId", string.IsNullOrEmpty(note.RemoteId) ? DBNull.Value : note.RemoteId);
                    cmd.Parameters.AddWithValue("@Title", note.Title ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Text", note.Text ?? string.Empty);
                    cmd.Parameters.AddWithValue("@X", double.IsNaN(note.X) ? DBNull.Value : note.X);
                    cmd.Parameters.AddWithValue("@Y", double.IsNaN(note.Y) ? DBNull.Value : note.Y);
                    cmd.Parameters.AddWithValue("@Width", note.Width);
                    cmd.Parameters.AddWithValue("@Height", note.Height);
                    cmd.Parameters.AddWithValue("@ExpandedHeight", note.ExpandedHeight > 0 ? note.ExpandedHeight : 340);
                    cmd.Parameters.AddWithValue("@IsCollapsed", note.IsCollapsed ? 1 : 0);
                    cmd.Parameters.AddWithValue("@IsArchived", note.IsArchived ? 1 : 0);
                    cmd.Parameters.AddWithValue("@IsFloating", note.IsFloating ? 1 : 0);
                    cmd.Parameters.AddWithValue("@SortOrder", note.SortOrder);
                    cmd.Parameters.AddWithValue("@ThemeId", note.ThemeId ?? "Yellow");
                    cmd.Parameters.AddWithValue("@IsTopmost", note.IsTopmost ? 1 : 0);
                    cmd.Parameters.AddWithValue("@TapeAngle", note.TapeAngle);
                    cmd.Parameters.AddWithValue("@FontFamily", note.FontFamily ?? "Malgun Gothic");
                    cmd.Parameters.AddWithValue("@FontSize", note.FontSize);
                    cmd.Parameters.AddWithValue("@IsBold", note.IsBold ? 1 : 0);
                    cmd.Parameters.AddWithValue("@CreatedAt", note.CreatedAt.ToString("o"));
                    cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                 transaction.Commit();
            }
            finally
            {
                WriteLock.Release();
            }
        }

        public bool TrySaveOrUpdateNotesBatchSync(IEnumerable<NoteModel> notes, TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero || !WriteLock.Wait(timeout))
            {
                return false;
            }

            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();

                var sql = @"
                    INSERT INTO Notes (Id, RemoteId, Title, Text, X, Y, Width, Height, ExpandedHeight, IsCollapsed, IsArchived, IsFloating, SortOrder, ThemeId, IsTopmost, TapeAngle, FontFamily, FontSize, IsBold, CreatedAt, UpdatedAt)
                    VALUES (@Id, @RemoteId, @Title, @Text, @X, @Y, @Width, @Height, @ExpandedHeight, @IsCollapsed, @IsArchived, @IsFloating, @SortOrder, @ThemeId, @IsTopmost, @TapeAngle, @FontFamily, @FontSize, @IsBold, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        RemoteId = excluded.RemoteId,
                        Title = excluded.Title,
                        Text = excluded.Text,
                        X = excluded.X,
                        Y = excluded.Y,
                        Width = excluded.Width,
                        Height = excluded.Height,
                        ExpandedHeight = excluded.ExpandedHeight,
                        IsCollapsed = excluded.IsCollapsed,
                        IsArchived = excluded.IsArchived,
                        IsFloating = excluded.IsFloating,
                        SortOrder = excluded.SortOrder,
                        ThemeId = excluded.ThemeId,
                        IsTopmost = excluded.IsTopmost,
                        TapeAngle = excluded.TapeAngle,
                        FontFamily = excluded.FontFamily,
                        FontSize = excluded.FontSize,
                        IsBold = excluded.IsBold,
                        UpdatedAt = excluded.UpdatedAt;";

                foreach (var note in notes)
                {
                    using var cmd = new SqliteCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@Id", note.Id);
                    cmd.Parameters.AddWithValue("@RemoteId", string.IsNullOrEmpty(note.RemoteId) ? DBNull.Value : note.RemoteId);
                    cmd.Parameters.AddWithValue("@Title", note.Title ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Text", note.Text ?? string.Empty);
                    cmd.Parameters.AddWithValue("@X", double.IsNaN(note.X) ? DBNull.Value : note.X);
                    cmd.Parameters.AddWithValue("@Y", double.IsNaN(note.Y) ? DBNull.Value : note.Y);
                    cmd.Parameters.AddWithValue("@Width", note.Width);
                    cmd.Parameters.AddWithValue("@Height", note.Height);
                    cmd.Parameters.AddWithValue("@ExpandedHeight", note.ExpandedHeight > 0 ? note.ExpandedHeight : 340);
                    cmd.Parameters.AddWithValue("@IsCollapsed", note.IsCollapsed ? 1 : 0);
                    cmd.Parameters.AddWithValue("@IsArchived", note.IsArchived ? 1 : 0);
                    cmd.Parameters.AddWithValue("@IsFloating", note.IsFloating ? 1 : 0);
                    cmd.Parameters.AddWithValue("@SortOrder", note.SortOrder);
                    cmd.Parameters.AddWithValue("@ThemeId", note.ThemeId ?? "Yellow");
                    cmd.Parameters.AddWithValue("@IsTopmost", note.IsTopmost ? 1 : 0);
                    cmd.Parameters.AddWithValue("@TapeAngle", note.TapeAngle);
                    cmd.Parameters.AddWithValue("@FontFamily", string.IsNullOrEmpty(note.FontFamily) ? "Malgun Gothic" : note.FontFamily);
                    cmd.Parameters.AddWithValue("@FontSize", note.FontSize > 0 ? note.FontSize : 13.5);
                    cmd.Parameters.AddWithValue("@IsBold", note.IsBold ? 1 : 0);
                    cmd.Parameters.AddWithValue("@CreatedAt", note.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@UpdatedAt", note.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            finally
            {
                WriteLock.Release();
            }
        }

        public async Task DeleteNoteAsync(string id)
        {
            await WriteLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var connection = new SqliteConnection(ConnectionString);
                await connection.OpenAsync().ConfigureAwait(false);

                using var cmd = new SqliteCommand("DELETE FROM Notes WHERE Id = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", id);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally
            {
                WriteLock.Release();
            }
        }
    }
}
