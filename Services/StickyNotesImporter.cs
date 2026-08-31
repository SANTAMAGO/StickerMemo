using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using StickerMemo.Models;

namespace StickerMemo.Services
{
    public class ImportResult
    {
        public bool Success { get; set; }
        public int ImportedCount { get; set; }
        public int SkippedCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<NoteModel> Notes { get; set; } = new();
    }

    public static class StickyNotesImporter
    {
        public static string GetStickyNotesDbPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(
                localAppData,
                "Packages",
                "Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe",
                "LocalState",
                "plum.sqlite"
            );
        }

        public static bool IsStickyNotesDbAvailable()
        {
            return File.Exists(GetStickyNotesDbPath());
        }

        public static ImportResult ImportNotes(HashSet<string>? existingRemoteIds = null)
        {
            var result = new ImportResult();
            var dbPath = GetStickyNotesDbPath();
            var seenRemoteIds = existingRemoteIds != null
                ? new HashSet<string>(existingRemoteIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(dbPath))
            {
                result.Success = false;
                result.Message = "Windows Sticky Notes 데이터를 찾을 수 없습니다.";
                return result;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "StickerMemo", "Import");
            var tempDb = Path.Combine(tempDir, $"plum_{Guid.NewGuid():N}.sqlite");

            try
            {
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                // 1. Copy plum.sqlite and WAL/SHM companion files to temp directory for read-only isolation
                File.Copy(dbPath, tempDb, true);

                var walPath = dbPath + "-wal";
                var tempWal = tempDb + "-wal";
                if (File.Exists(walPath))
                {
                    try { File.Copy(walPath, tempWal, true); } catch { }
                }

                var shmPath = dbPath + "-shm";
                var tempShm = tempDb + "-shm";
                if (File.Exists(shmPath))
                {
                    try { File.Copy(shmPath, tempShm, true); } catch { }
                }

                // 2. Open temporary copy in ReadOnly mode
                using var connection = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly");
                connection.Open();

                // 3. Schema Auto-Detection via PRAGMA table_info(Note)
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var pragmaCmd = new SqliteCommand("PRAGMA table_info(Note);", connection))
                using (var pragmaReader = pragmaCmd.ExecuteReader())
                {
                    while (pragmaReader.Read())
                    {
                        var colName = pragmaReader.GetString(1); // column name is at index 1
                        columns.Add(colName);
                    }
                }

                if (columns.Count == 0 || !columns.Contains("Text"))
                {
                    result.Success = false;
                    result.Message = "Windows Sticky Notes 테이블 구조를 인식할 수 없습니다.";
                    return result;
                }

                // 4. Build dynamic SELECT statement based on existing columns
                var selectFields = new List<string>();

                // ID column
                string? idCol = columns.Contains("Id") ? "Id" : (columns.Contains("RemoteId") ? "RemoteId" : null);
                if (idCol != null) selectFields.Add(idCol); else selectFields.Add("NULL AS Id");

                selectFields.Add("Text");
                selectFields.Add(columns.Contains("Theme") ? "Theme" : "'Yellow' AS Theme");
                selectFields.Add(columns.Contains("CreatedAt") ? "CreatedAt" : "NULL AS CreatedAt");
                selectFields.Add(columns.Contains("UpdatedAt") ? "UpdatedAt" : "NULL AS UpdatedAt");

                // Determine WHERE condition for non-deleted notes
                var whereClause = string.Empty;
                if (columns.Contains("DeletedAt"))
                {
                    whereClause = " WHERE DeletedAt IS NULL";
                }
                else if (columns.Contains("IsDeleted"))
                {
                    whereClause = " WHERE IsDeleted = 0";
                }

                var sql = $"SELECT {string.Join(", ", selectFields)} FROM Note{whereClause};";

                using var command = new SqliteCommand(sql, connection);
                using var reader = command.ExecuteReader();
                var random = new Random();

                while (reader.Read())
                {
                    var remoteId = reader.IsDBNull(0) ? null : reader.GetString(0);
                    var rawText = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    var themeName = reader.IsDBNull(2) ? "Yellow" : reader.GetString(2);
                    var rawCreatedAt = reader.IsDBNull(3) ? null : reader.GetValue(3);
                    var rawUpdatedAt = reader.IsDBNull(4) ? null : reader.GetValue(4);

                    // Deduplication check
                    if (!string.IsNullOrEmpty(remoteId) && seenRemoteIds.Contains(remoteId))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Parse text safely
                    var cleanText = StickyNoteTextParser.Parse(rawText);
                    if (string.IsNullOrWhiteSpace(cleanText))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(remoteId))
                    {
                        seenRemoteIds.Add(remoteId);
                    }

                    var angle = (random.NextDouble() * 3.0) - 1.5;

                    var note = new NoteModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        RemoteId = remoteId,
                        Text = cleanText,
                        ThemeId = ThemeColors.MapWindowsStickyTheme(themeName),
                        TapeAngle = Math.Round(angle, 1),
                        CreatedAt = ParseDateTimeSafe(rawCreatedAt),
                        UpdatedAt = ParseDateTimeSafe(rawUpdatedAt)
                    };

                    result.Notes.Add(note);
                    result.ImportedCount++;
                }

                result.Success = true;
                if (result.SkippedCount > 0)
                {
                    result.Message = $"Windows Sticky Notes에서 {result.ImportedCount}개의 메모를 가져왔습니다. ({result.SkippedCount}개 기존 메모 건너뜀)";
                }
                else
                {
                    result.Message = $"Windows Sticky Notes에서 {result.ImportedCount}개의 메모를 가져왔습니다.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sticky Notes import exception: {ex}");
                result.Success = false;
                result.Message = "Windows Sticky Notes 데이터를 가져오는 중 오류가 발생했습니다.";
            }
            finally
            {
                // Clean up temporary database files
                try
                {
                    if (File.Exists(tempDb)) File.Delete(tempDb);
                    if (File.Exists(tempDb + "-wal")) File.Delete(tempDb + "-wal");
                    if (File.Exists(tempDb + "-shm")) File.Delete(tempDb + "-shm");
                }
                catch { }
            }

            return result;
        }

        /// <summary>
        /// Safely parses heterogeneous date formats (Unix timestamp, .NET ticks, ISO8601 strings) with fallback.
        /// </summary>
        private static DateTime ParseDateTimeSafe(object? raw)
        {
            if (raw == null || raw is DBNull)
            {
                return DateTime.Now;
            }

            try
            {
                if (raw is long longVal)
                {
                    // .NET ticks are much larger than Unix values, so test them first.
                    if (longVal >= new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks &&
                        longVal <= DateTime.UtcNow.AddYears(2).Ticks)
                    {
                        return new DateTime(longVal, DateTimeKind.Utc).ToLocalTime();
                    }

                    var minUnixSeconds = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
                    var maxUnixSeconds = DateTimeOffset.UtcNow.AddYears(2).ToUnixTimeSeconds();
                    var minUnixMilliseconds = minUnixSeconds * 1000L;
                    var maxUnixMilliseconds = maxUnixSeconds * 1000L;

                    if (longVal >= minUnixMilliseconds && longVal <= maxUnixMilliseconds)
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(longVal).LocalDateTime;
                    }
                    if (longVal >= minUnixSeconds && longVal <= maxUnixSeconds)
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(longVal).LocalDateTime;
                    }

                    return DateTime.Now;
                }

                if (raw is double doubleVal)
                {
                    return ParseDateTimeSafe((long)doubleVal);
                }

                var str = raw.ToString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) && IsSaneDate(dt))
                    {
                        return dt;
                    }
                    if (long.TryParse(str, out var parsedLong))
                    {
                        return ParseDateTimeSafe(parsedLong);
                    }
                }
            }
            catch
            {
                // Fallback to Now
            }

            return DateTime.Now;
        }

        private static bool IsSaneDate(DateTime value)
        {
            return value >= new DateTime(2000, 1, 1) && value <= DateTime.Now.AddYears(2);
        }
    }
}
