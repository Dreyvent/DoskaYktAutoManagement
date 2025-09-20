using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    public static class DatabaseHelper
    {
        private static readonly string _dbPath =
            (Environment.GetEnvironmentVariable("ADS_DB_PATH") ??
             Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ads.db"));

        private static readonly string _connectionString =
            $"Data Source={_dbPath}";

        static DatabaseHelper()
        {
            // Инициализация БД синхронно, чтобы избежать потенциальных deadlock'ов при .Wait() в UI контексте
            InitializeDatabase();
        }

        private static void InitializeDatabase()
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
                    Content TEXT NOT NULL,
                    Cycle INTEGER NOT NULL,
                    IsAuto INTEGER NOT NULL,
                    AccountId INTEGER NOT NULL,
                    AccountLogin TEXT NOT NULL,
                    SiteId TEXT NOT NULL DEFAULT '',
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
            while (reader2.Read())
            {
                if (reader2.GetString(1).Equals("SiteId", StringComparison.OrdinalIgnoreCase))
                {
                    hasSiteId = true;
                    break;
                }
            }
            reader2.Close();
            if (!hasSiteId)
            {
                command.CommandText = "ALTER TABLE Announcements ADD COLUMN SiteId TEXT NOT NULL DEFAULT '';";
                try { command.ExecuteNonQuery(); } catch { }
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
        public static async Task<List<(int id, string title, string content, int cycle, int isAuto, int accountId, string accountLogin, string siteId)>> GetAnnouncementsAsync()
        {
            var result = new List<(int, string, string, int, int, int, string, string)>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title, Content, Cycle, IsAuto, AccountId, AccountLogin, SiteId FROM Announcements;";

            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                result.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
                ));
            }

            return result;
        }

        public static async Task<int> AddAnnouncementAsync(string title, int cycle, bool isAuto, int accountId, string accountLogin, string siteId = "")
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Announcements (Title, Content, Cycle, IsAuto, AccountId, AccountLogin, SiteId)
                VALUES ($title, $content, $cycle, $isAuto, $accountId, $accountLogin, $siteId);
                SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$content", ""); // мы храним пустой контент — или можно убрать колонку
            command.Parameters.AddWithValue("$cycle", cycle);
            command.Parameters.AddWithValue("$isAuto", isAuto ? 1 : 0);
            command.Parameters.AddWithValue("$accountId", accountId);
            command.Parameters.AddWithValue("$accountLogin", accountLogin);
            command.Parameters.AddWithValue("$siteId", siteId);

            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return Convert.ToInt32(result);
        }

        public static async Task UpdateAnnouncementAsync(int id, string title, string content, int cycle, bool isAuto, int accountId, string accountLogin, string siteId = "")
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Announcements
                SET Title = $title,
                    Content = $content,
                    Cycle = $cycle,
                    IsAuto = $isAuto,
                    AccountId = $accountId,
                    AccountLogin = $accountLogin,
                    SiteId = $siteId
                WHERE Id = $id;";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$content", content);
            command.Parameters.AddWithValue("$cycle", cycle);
            command.Parameters.AddWithValue("$isAuto", isAuto ? 1 : 0);
            command.Parameters.AddWithValue("$accountId", accountId);
            command.Parameters.AddWithValue("$accountLogin", accountLogin);
            command.Parameters.AddWithValue("$siteId", siteId);
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
