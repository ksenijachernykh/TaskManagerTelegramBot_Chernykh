using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManagerTelegramBot_Chernykh.Classes
{
    public class DatabaseManager : IDisposable
    {
        private readonly string _connectionString;
        private MySqlConnection _connection;

        public DatabaseManager(string connectionString)
        {
            _connectionString = connectionString;
            _connection = new MySqlConnection(_connectionString);
        }

        public async Task OpenConnectionAsync()
        {
            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }
        }

        public async Task CloseConnectionAsync()
        {
            if (_connection.State != ConnectionState.Closed)
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<int> GetOrCreateUserAsync(long telegramId)
        {
            await OpenConnectionAsync();

            var checkCmd = new MySqlCommand(
                "SELECT id FROM users WHERE telegram_id = @telegramId",
                _connection);
            checkCmd.Parameters.AddWithValue("@telegramId", telegramId);

            var result = await checkCmd.ExecuteScalarAsync();

            if (result != null)
            {
                return Convert.ToInt32(result);
            }

            var insertCmd = new MySqlCommand(
                "INSERT INTO users (telegram_id) VALUES (@telegramId); SELECT LAST_INSERT_ID();",
                _connection);
            insertCmd.Parameters.AddWithValue("@telegramId", telegramId);

            var userId = await insertCmd.ExecuteScalarAsync();
            return Convert.ToInt32(userId);
        }

        public async Task<int> AddEventAsync(int userId, DateTime eventTime, string message)
        {
            await OpenConnectionAsync();

            var cmd = new MySqlCommand(
                "INSERT INTO events (user_id, event_time, message) VALUES (@userId, @eventTime, @message); SELECT LAST_INSERT_ID();",
                _connection);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@eventTime", eventTime);
            cmd.Parameters.AddWithValue("@message", message);

            var eventId = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(eventId);
        }

        public async Task<List<EventDb>> GetUserEventsAsync(long telegramId)
        {
            await OpenConnectionAsync();

            var events = new List<EventDb>();
            var cmd = new MySqlCommand(
                @"SELECT e.id, e.event_time, e.message 
                  FROM events e 
                  INNER JOIN users u ON e.user_id = u.id 
                  WHERE u.telegram_id = @telegramId 
                  ORDER BY e.event_time",
                _connection);
            cmd.Parameters.AddWithValue("@telegramId", telegramId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                events.Add(new EventDb
                {
                    Id = reader.GetInt32("id"),
                    Time = reader.GetDateTime("event_time"),
                    Message = reader.GetString("message")
                });
            }

            return events;
        }

        public async Task<bool> DeleteEventAsync(int eventId)
        {
            await OpenConnectionAsync();

            var cmd = new MySqlCommand(
                "DELETE FROM events WHERE id = @eventId",
                _connection);
            cmd.Parameters.AddWithValue("@eventId", eventId);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }

        public async Task<bool> DeleteAllUserEventsAsync(long telegramId)
        {
            await OpenConnectionAsync();

            var cmd = new MySqlCommand(
                @"DELETE e FROM events e 
                  INNER JOIN users u ON e.user_id = u.id 
                  WHERE u.telegram_id = @telegramId",
                _connection);
            cmd.Parameters.AddWithValue("@telegramId", telegramId);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }

        public async Task<List<ReminderEvent>> GetEventsForReminderAsync(DateTime currentTime)
        {
            await OpenConnectionAsync();

            var events = new List<ReminderEvent>();
            var cmd = new MySqlCommand(
                @"SELECT u.telegram_id, e.message 
                  FROM events e 
                  INNER JOIN users u ON e.user_id = u.id 
                  WHERE DATE_FORMAT(e.event_time, '%Y-%m-%d %H:%i') = @currentTime",
                _connection);
            cmd.Parameters.AddWithValue("@currentTime", currentTime.ToString("yyyy-MM-dd HH:mm"));

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                events.Add(new ReminderEvent
                {
                    TelegramId = reader.GetInt64("telegram_id"),
                    Message = reader.GetString("message")
                });
            }

            return events;
        }

        public async Task DeleteEventsAfterReminderAsync(DateTime currentTime)
        {
            await OpenConnectionAsync();

            var cmd = new MySqlCommand(
                @"DELETE e FROM events e 
                  WHERE DATE_FORMAT(e.event_time, '%Y-%m-%d %H:%i') = @currentTime",
                _connection);
            cmd.Parameters.AddWithValue("@currentTime", currentTime.ToString("yyyy-MM-dd HH:mm"));

            await cmd.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }

        public async Task<bool> DeleteEventByMessageAsync(string message)
        {
            await OpenConnectionAsync();

            var cmd = new MySqlCommand(
                "DELETE FROM events WHERE message = @message",
                _connection);
            cmd.Parameters.AddWithValue("@message", message);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }
    }

    public class EventDb
    {
        public int Id { get; set; }
        public DateTime Time { get; set; }
        public string Message { get; set; }
    }

    public class ReminderEvent
    {
        public long TelegramId { get; set; }
        public string Message { get; set; }
    }
}
