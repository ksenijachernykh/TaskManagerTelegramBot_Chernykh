using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace TaskManagerTelegramBot_Chernykh.Classes
{
    // ЕДИНСТВЕННЫЙ КЛАСС ДЛЯ ОБЫЧНОЙ ЗАДАЧИ
    public class EventDb
    {
        public int Id { get; set; }
        public DateTime Time { get; set; }
        public string Message { get; set; }
    }

    // ЕДИНСТВЕННЫЙ КЛАСС ДЛЯ ПОВТОРЯЮЩЕЙСЯ ЗАДАЧИ
    public class RecurringTask
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; }
        public string ScheduleType { get; set; }
        public string ScheduleData { get; set; }
        public TimeSpan Time { get; set; }
        public bool IsActive { get; set; }
        public long TelegramId { get; set; }
    }

    // КЛАСС ДЛЯ НАПОМИНАНИЙ
    public class ReminderEvent
    {
        public long TelegramId { get; set; }
        public string Message { get; set; }
    }

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

            Console.WriteLine($"Добавляем задачу: userId={userId}, время={eventTime:yyyy-MM-dd HH:mm:ss}, сообщение='{message}'");

            var cmd = new MySqlCommand(
                "INSERT INTO events (user_id, event_time, message) VALUES (@userId, @eventTime, @message); SELECT LAST_INSERT_ID();",
                _connection);

            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@eventTime", eventTime);
            cmd.Parameters.AddWithValue("@message", message);

            var eventId = await cmd.ExecuteScalarAsync();

            Console.WriteLine($"Задача добавлена с ID: {eventId}");

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
                  AND e.event_time > NOW()
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

            Console.WriteLine($"Найдено задач для пользователя {telegramId}: {events.Count}");

            return events;
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

            // Форматируем время для сравнения (только дата и время до минут)
            string formattedTime = currentTime.ToString("yyyy-MM-dd HH:mm");
            Console.WriteLine($"Ищем задачи на время: {formattedTime}");

            var cmd = new MySqlCommand(
                @"SELECT u.telegram_id, e.message 
                  FROM events e 
                  INNER JOIN users u ON e.user_id = u.id 
                  WHERE DATE_FORMAT(e.event_time, '%Y-%m-%d %H:%i') = @currentTime",
                _connection);

            cmd.Parameters.AddWithValue("@currentTime", formattedTime);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                events.Add(new ReminderEvent
                {
                    TelegramId = reader.GetInt64("telegram_id"),
                    Message = reader.GetString("message")
                });

                Console.WriteLine($"Найдена задача для напоминания: telegramId={reader.GetInt64("telegram_id")}, message={reader.GetString("message")}");
            }

            return events;
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

        public async Task<int> CreateRecurringTaskAsync(long telegramId, string message, string scheduleData, TimeSpan time)
        {
            await OpenConnectionAsync();

            int userId = await GetOrCreateUserAsync(telegramId);

            var cmd = new MySqlCommand(
                @"INSERT INTO recurring_tasks (user_id, message, schedule_type, schedule_data, time) 
                  VALUES (@userId, @message, 'weekly', @scheduleData, @time);
                  SELECT LAST_INSERT_ID();",
                _connection);

            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@message", message);
            cmd.Parameters.AddWithValue("@scheduleData", scheduleData);
            cmd.Parameters.AddWithValue("@time", time.ToString(@"hh\:mm\:ss"));

            var taskId = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(taskId);
        }

        public async Task<List<RecurringTask>> GetUserRecurringTasksAsync(long telegramId)
        {
            await OpenConnectionAsync();

            var tasks = new List<RecurringTask>();
            var cmd = new MySqlCommand(
                @"SELECT rt.*, u.telegram_id 
                  FROM recurring_tasks rt
                  INNER JOIN users u ON rt.user_id = u.id 
                  WHERE u.telegram_id = @telegramId AND rt.is_active = TRUE
                  ORDER BY rt.time",
                _connection);

            cmd.Parameters.AddWithValue("@telegramId", telegramId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try
                {
                    string timeString = reader["time"].ToString();
                    TimeSpan time = TimeSpan.Parse(timeString);

                    tasks.Add(new RecurringTask
                    {
                        Id = reader.GetInt32("id"),
                        UserId = reader.GetInt32("user_id"),
                        Message = reader.GetString("message"),
                        ScheduleType = reader.GetString("schedule_type"),
                        ScheduleData = reader.GetString("schedule_data"),
                        Time = time,
                        IsActive = reader.GetBoolean("is_active"),
                        TelegramId = reader.GetInt64("telegram_id")
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при чтении повторяющейся задачи: {ex.Message}");
                }
            }

            return tasks;
        }

        public async Task<List<RecurringTask>> GetRecurringTasksForTodayAsync(DateTime currentDate)
        {
            await OpenConnectionAsync();

            var tasks = new List<RecurringTask>();
            int currentDayOfWeek = (int)currentDate.DayOfWeek;

            Console.WriteLine($"Ищем повторяющиеся задачи для дня недели: {currentDayOfWeek}");

            var cmd = new MySqlCommand(
                @"SELECT rt.*, u.telegram_id 
                  FROM recurring_tasks rt
                  INNER JOIN users u ON rt.user_id = u.id 
                  WHERE rt.is_active = TRUE 
                  AND FIND_IN_SET(@dayOfWeek, rt.schedule_data) > 0",
                _connection);

            cmd.Parameters.AddWithValue("@dayOfWeek", currentDayOfWeek);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try
                {
                    string timeString = reader["time"].ToString();
                    TimeSpan time = TimeSpan.Parse(timeString);

                    var task = new RecurringTask
                    {
                        Id = reader.GetInt32("id"),
                        UserId = reader.GetInt32("user_id"),
                        Message = reader.GetString("message"),
                        ScheduleType = reader.GetString("schedule_type"),
                        ScheduleData = reader.GetString("schedule_data"),
                        Time = time,
                        IsActive = reader.GetBoolean("is_active"),
                        TelegramId = reader.GetInt64("telegram_id")
                    };

                    tasks.Add(task);
                    Console.WriteLine($"Найдена повторяющаяся задача: {task.Message} в {task.Time:hh\\:mm}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при чтении задачи для сегодня: {ex.Message}");
                }
            }

            return tasks;
        }

        public async Task<bool> DeleteRecurringTaskAsync(int taskId)
        {
            await OpenConnectionAsync();

            var cmd = new MySqlCommand(
                "DELETE FROM recurring_tasks WHERE id = @taskId",
                _connection);

            cmd.Parameters.AddWithValue("@taskId", taskId);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            return rowsAffected > 0;
        }

        public async Task<long> GetTelegramIdByUserIdAsync(int userId)
        {
            await OpenConnectionAsync();

            var cmd = new MySqlCommand(
                "SELECT telegram_id FROM users WHERE id = @userId",
                _connection);
            cmd.Parameters.AddWithValue("@userId", userId);

            var result = await cmd.ExecuteScalarAsync();
            return result != null ? Convert.ToInt64(result) : 0;
        }

        public MySqlConnection GetConnection()
        {
            return _connection;
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}