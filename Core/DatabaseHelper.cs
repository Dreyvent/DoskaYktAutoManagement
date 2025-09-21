using DoskaYkt_AutoManagement.MVVM.Model;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    public static class DatabaseHelper
    {
        // База данных сохраняется в AppData для работы из ProgramFiles без прав администратора
        private static readonly string _dbPath =
            (Environment.GetEnvironmentVariable("ADS_DB_PATH") ??
             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                         "DoskaYktAutoManagement", "ads.db"));

        private static readonly string _connectionString =
            $"Data Source={_dbPath};Cache=Shared;Default Timeout=30";

        static DatabaseHelper()
        {
            // Инициализация БД синхронно, чтобы избежать потенциальных deadlock'ов при .Wait() в UI контексте
            InitializeDatabase();
        }

        private static void InitializeDatabase()
        {
            try
            {
                // Ensure directory exists
                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Accounts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Login TEXT NOT NULL,
                    Password TEXT NOT NULL,
                    IsCurrent INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Announcements (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Cycle INTEGER NOT NULL,
                    IsAuto INTEGER NOT NULL,
                    AccountId INTEGER NOT NULL,
                    AccountLogin TEXT NOT NULL,
                    SiteId TEXT NOT NULL DEFAULT '',
                    IsPublishedOnSite INTEGER NOT NULL DEFAULT 0,
                    NextUnpublishAt TEXT NULL,
                    NextRepublishAt TEXT NULL,
                    FOREIGN KEY (AccountId) REFERENCES Accounts(Id)
                );

            ";
            command.ExecuteNonQuery();

            // Миграция: если поле IsCurrent отсутствует, добавить его
            command.CommandText = "PRAGMA table_info(Accounts);";
            using var reader = command.ExecuteReader();
            bool hasIsCurrent = false;
            while (reader.Read())
            {
                if (reader.GetString(1).Equals("IsCurrent", StringComparison.OrdinalIgnoreCase))
                {
                    hasIsCurrent = true;
                    break;
                }
            }
            reader.Close();
            if (!hasIsCurrent)
            {
                command.CommandText = "ALTER TABLE Accounts ADD COLUMN IsCurrent INTEGER NOT NULL DEFAULT 0;";
                try { command.ExecuteNonQuery(); } catch { }
            }

            // Миграция: если поле SiteId отсутствует, добавить его в Announcements
            command.CommandText = "PRAGMA table_info(Announcements);";
            using var reader2 = command.ExecuteReader();
            bool hasSiteId = false;
            bool hasIsPublishedOnSite = false;
            bool hasNextUnpublishAt = false;
            bool hasNextRepublishAt = false;
            while (reader2.Read())
            {
                var columnName = reader2.GetString(1);
                if (columnName.Equals("SiteId", StringComparison.OrdinalIgnoreCase))
                    hasSiteId = true;
                else if (columnName.Equals("IsPublishedOnSite", StringComparison.OrdinalIgnoreCase))
                    hasIsPublishedOnSite = true;
                else if (columnName.Equals("NextUnpublishAt", StringComparison.OrdinalIgnoreCase))
                    hasNextUnpublishAt = true;
                else if (columnName.Equals("NextRepublishAt", StringComparison.OrdinalIgnoreCase))
                    hasNextRepublishAt = true;
            }
            reader2.Close();
            
            if (!hasSiteId)
            {
                command.CommandText = "ALTER TABLE Announcements ADD COLUMN SiteId TEXT NOT NULL DEFAULT '';";
                try { command.ExecuteNonQuery(); } catch { }
            }
            if (!hasIsPublishedOnSite)
            {
                command.CommandText = "ALTER TABLE Announcements ADD COLUMN IsPublishedOnSite INTEGER NOT NULL DEFAULT 0;";
                try { command.ExecuteNonQuery(); } catch { }
            }
            if (!hasNextUnpublishAt)
            {
                command.CommandText = "ALTER TABLE Announcements ADD COLUMN NextUnpublishAt TEXT NULL;";
                try { command.ExecuteNonQuery(); } catch { }
            }
            if (!hasNextRepublishAt)
            {
                command.CommandText = "ALTER TABLE Announcements ADD COLUMN NextRepublishAt TEXT NULL;";
                try { command.ExecuteNonQuery(); } catch { }
            }
            }
            catch (Exception ex)
            {
                // Логируем ошибку инициализации БД
                System.Diagnostics.Debug.WriteLine($"[DatabaseHelper] Ошибка инициализации БД: {ex.Message}");
                throw;
            }
        }

        // ==================
        // Accounts
        // ==================
        public static async Task<List<(int id, string login, string password, bool isCurrent)>> GetAccountsAsync()
        {
            var result = new List<(int, string, string, bool)>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Login, Password, IsCurrent FROM Accounts;";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1));
            }

            return result;
        }

        public static async Task<int> AddAccountAsync(string login, string password, bool isCurrent = false)
        {
            if (string.IsNullOrWhiteSpace(login))
                throw new ArgumentException("Логин не может быть пустым", nameof(login));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Пароль не может быть пустым", nameof(password));

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO Accounts (Login, Password, IsCurrent) VALUES ($login, $password, $isCurrent); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$login", login);
            command.Parameters.AddWithValue("$password", password);
            command.Parameters.AddWithValue("$isCurrent", isCurrent ? 1 : 0);

            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return Convert.ToInt32(result);
        }

        public static async Task DeleteAccountAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Accounts WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public static async Task UpdateAccountAsync(int id, string login, string password)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Accounts
                SET Login = $login, Password = $password
                WHERE Id = $id;";
            command.Parameters.AddWithValue("$login", login);
            command.Parameters.AddWithValue("$password", password);
            command.Parameters.AddWithValue("$id", id);

            await command.ExecuteNonQueryAsync();
        }

        public static async Task SetCurrentAccountAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            // Сбросить все IsCurrent
            command.CommandText = "UPDATE Accounts SET IsCurrent = 0;";
            await command.ExecuteNonQueryAsync();
            // Установить IsCurrent для выбранного
            command.CommandText = "UPDATE Accounts SET IsCurrent = 1 WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
        }

        public static async Task<(int id, string login, string password)?> GetCurrentAccountAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Login, Password FROM Accounts WHERE IsCurrent = 1 LIMIT 1;";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
            }
            return null;
        }

        // ==================
        // Announcements
        // ==================
        public static async Task<List<(int id, string title, int cycle, int isAuto, int accountId, string accountLogin, string siteId, bool isPublishedOnSite, string nextUnpublishAt, string nextRepublishAt)>> GetAnnouncementsAsync()
        {
            var result = new List<(int, string, int, int, int, string, string, bool, string, string)>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title, Cycle, IsAuto, AccountId, AccountLogin, SiteId, IsPublishedOnSite, NextUnpublishAt, NextRepublishAt FROM Announcements;";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    reader.GetInt32(7) == 1,
                    reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    reader.IsDBNull(9) ? string.Empty : reader.GetString(9)
                ));
            }

            return result;
        }

        public static async Task<int> AddAnnouncementAsync(
            string title,
            int cycle,
            bool isAuto,
            int accountId,
            string accountLogin,
            string siteId = "",
            bool isPublishedOnSite = false,
            string? nextUnpublishAt = null,
            string? nextRepublishAt = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Announcements 
                (Title, Cycle, IsAuto, AccountId, AccountLogin, SiteId, IsPublishedOnSite, NextUnpublishAt, NextRepublishAt)
                VALUES ($title, $cycle, $isAuto, $accountId, $accountLogin, $siteId, $isPublishedOnSite, $nextUnpublishAt, $nextRepublishAt);
                SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$cycle", cycle);
            command.Parameters.AddWithValue("$isAuto", isAuto ? 1 : 0);
            command.Parameters.AddWithValue("$accountId", accountId);
            command.Parameters.AddWithValue("$accountLogin", accountLogin);
            command.Parameters.AddWithValue("$siteId", siteId);
            command.Parameters.AddWithValue("$isPublishedOnSite", isPublishedOnSite ? 1 : 0);
            command.Parameters.AddWithValue("$nextUnpublishAt", (object?)nextUnpublishAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$nextRepublishAt", (object?)nextRepublishAt ?? DBNull.Value);

            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return Convert.ToInt32(result);
        }

        public static async Task UpdateAnnouncementAsync(
            int id,
            string title,
            int cycle,
            bool isAuto,
            int accountId,
            string accountLogin,
            string siteId = "",
            bool isPublishedOnSite = false,
            string? nextUnpublishAt = null,
            string? nextRepublishAt = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Announcements
                SET Title = $title,
                    Cycle = $cycle,
                    IsAuto = $isAuto,
                    AccountId = $accountId,
                    AccountLogin = $accountLogin,
                    SiteId = $siteId,
                    IsPublishedOnSite = $isPublishedOnSite,
                    NextUnpublishAt = $nextUnpublishAt,
                    NextRepublishAt = $nextRepublishAt
                WHERE Id = $id;";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$cycle", cycle);
            command.Parameters.AddWithValue("$isAuto", isAuto ? 1 : 0);
            command.Parameters.AddWithValue("$accountId", accountId);
            command.Parameters.AddWithValue("$accountLogin", accountLogin);
            command.Parameters.AddWithValue("$siteId", siteId);
            command.Parameters.AddWithValue("$isPublishedOnSite", isPublishedOnSite ? 1 : 0);
            command.Parameters.AddWithValue("$nextUnpublishAt", (object?)nextUnpublishAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$nextRepublishAt", (object?)nextRepublishAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$id", id);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public static async Task DeleteAnnouncementAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Announcements WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);

            await command.ExecuteNonQueryAsync();
        }
    }
}
