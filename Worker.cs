using TaskManagerTelegramBot_Chernykh.Classes;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text;

namespace TaskManagerTelegramBot_Chernykh
{
    public class Worker : BackgroundService
    {
        readonly string Token = "8362919085:AAGFGKv_nsqLx7iUSlV6J7kgD4U9Mrp_arM";
        readonly string ConnectionString = "Server=localhost;Database=taskmanagerbot;User=root;Password=;";

        TelegramBotClient TelegramBotClient;
        DatabaseManager DatabaseManager;
        System.Threading.Timer Timer;

        List<string> Messages = new List<string>()
        {
            "Здравствуйте! " +
                "\nРады приветствовать вас в Telegram-боте «Напоминатор»!" +
                "\nНаш бот создан для того, чтобы напоминать вам о важных событиях и мероприятиях. С ним вы точно не пропустите ничего важного!" +
                "\nНе забудьте добавить бота в список своих контактов и настроить уведомления. Тогда вы всегда будете в курсе событий!",

            "\nУкажите дату и время напоминания в следующем формате:" +
                "\n<i><b>12:51 26.04.2025</b>" +
                "\nНапомни о том что я хотел сходить в магазин.</i>",

            "\nКажется, что-то не получилось." +
                "\nУкажите дату и время напоминания в следующем формате:" +
                "\n<i><b>12:51 26.04.2025</b>" +
                "\nНапомни о том что я хотел сходить в магазин.</i>",

            "\nЗадачи пользователя не найдены.",
            "Событие удалено.",
            "Все события удалены."
        };

        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                DatabaseManager = new DatabaseManager(ConnectionString);
                await DatabaseManager.OpenConnectionAsync();
                _logger.LogInformation("Подключение к базе данных установлено");

                TelegramBotClient = new TelegramBotClient(Token);

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                TelegramBotClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandlePollingErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken
                );

                Timer = new System.Threading.Timer(
                    callback: CheckRemindersCallback,
                    state: null,
                    dueTime: TimeSpan.Zero,
                    period: TimeSpan.FromMinutes(1));

                _logger.LogInformation("Бот запущен и готов к работе");

                var me = await TelegramBotClient.GetMe(cancellationToken: stoppingToken);
                _logger.LogInformation($"Бот @{me.Username} запущен");

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске бота");
                throw;
            }
        }

        public bool CheckFormatDateTime(string value, out DateTime time)
        {
            string[] formats = new[]
            {
                "HH:mm dd.MM.yyyy",
                "H:mm dd.MM.yyyy",
                "HH:mm d.MM.yyyy",
                "H:mm d.MM.yyyy"
            };

            return DateTime.TryParseExact(value, formats, null,
                System.Globalization.DateTimeStyles.None, out time);
        }

        private static ReplyKeyboardMarkup GetButtons()
        {
            List<KeyboardButton> keyboardButton = new List<KeyboardButton>();
            keyboardButton.Add(new KeyboardButton("Удалить все задачи"));
            return new ReplyKeyboardMarkup
            {
                Keyboard = new List<List<KeyboardButton>>()
                {
                    keyboardButton
                }
            };
        }

        
        public InlineKeyboardMarkup GetDeleteButton(int taskId)
        {
            
            string callbackData = $"del_{taskId}";

            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Удалить", callbackData)
                }
            });
        }

        public async Task SendMessage(long chatId, int typeMessage)
        {
            if (typeMessage != 3)
            {
                await TelegramBotClient.SendMessage(
                    chatId,
                    Messages[typeMessage],
                    ParseMode.Html,
                    replyMarkup: GetButtons());
            }
            else if (typeMessage == 3)
            {
                await TelegramBotClient.SendMessage(
                    chatId,
                    $"Указанное время и дата не могут быть установлены, " +
                    $"потому что сейчас уже: {DateTime.Now.ToString("HH.mm dd.MM.yyyy")}");
            }
        }

        public async Task Command(long chatId, string command)
        {
            if (command.ToLower() == "/start")
                await SendMessage(chatId, 0);
            else if (command.ToLower() == "/create_task")
                await SendMessage(chatId, 1);
            else if (command.ToLower() == "/list_tasks")
            {
                var tasks = await DatabaseManager.GetUserEventsAsync(chatId);

                if (tasks == null || tasks.Count == 0)
                    await SendMessage(chatId, 3);
                else
                {
                    foreach (var task in tasks)
                    {
                        await TelegramBotClient.SendMessage(
                            chatId,
                            $"Напоминание\n" +
                            $"Время: {task.Time.ToString("HH:mm dd.MM.yyyy")}\n" +
                            $"Сообщение: {task.Message}",
                            parseMode: ParseMode.Markdown,
                            replyMarkup: GetDeleteButton(task.Id) 
                        );
                    }
                }
            }
            else if (command.ToLower() == "/recurring_tasks")
            {
                await ShowRecurringTasks(chatId);
            }
        }

        private async Task ShowRecurringTasks(long chatId)
        {
            var tasks = await DatabaseManager.GetUserRecurringTasksAsync(chatId);

            if (tasks == null || tasks.Count == 0)
            {
                await TelegramBotClient.SendMessage(
                    chatId,
                    "У вас нет повторяющихся задач.",
                    replyMarkup: GetButtons());
                return;
            }

            await TelegramBotClient.SendMessage(
                chatId,
                $"Ваши повторяющиеся задачи ({tasks.Count}):",
                replyMarkup: GetButtons());

            foreach (var task in tasks)
            {
                string scheduleInfo = GetScheduleInfo(task);
                await TelegramBotClient.SendMessage(
                    chatId,
                    $"Повторяющаяся задача\n" +
                    $"Время: {task.Time:hh\\:mm}\n" +
                    $"Сообщение: {task.Message}\n" +
                    $"{scheduleInfo}",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: GetDeleteRecurringButton(task.Id));
            }
        }

        private string GetScheduleInfo(RecurringTask task)
        {
            try
            {
                var days = task.ScheduleData.Split(',').Select(int.Parse);
                var dayNames = days.Select(d => GetDayName(d)).ToList();
                return "Повторяется по: " + string.Join(", ", dayNames);
            }
            catch
            {
                return $"Повторяется по расписанию: {task.ScheduleData}";
            }
        }

        private string GetDayName(int dayNumber)
        {
            return dayNumber switch
            {
                0 => "воскресеньям",
                1 => "понедельникам",
                2 => "вторникам",
                3 => "средам",
                4 => "четвергам",
                5 => "пятницам",
                6 => "субботам",
                _ => "неизвестным дням"
            };
        }

        public InlineKeyboardMarkup GetDeleteRecurringButton(int taskId)
        {
            
            string callbackData = $"recurring_{taskId}";

            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Удалить повторяющуюся", callbackData)
                }
            });
        }

        private async Task GetMessage(Message message)
        {
            Console.WriteLine("Получено сообщение: " + message.Text + " от пользователя: " + message.Chat.Username);
            long chatId = message.Chat.Id;
            string text = message.Text?.Trim() ?? "";

            if (text.Contains("/"))
                await Command(chatId, text);
            else if (text.Equals("Удалить все задачи"))
            {
                bool success = await DatabaseManager.DeleteAllUserEventsAsync(message.Chat.Id);
                if (!success)
                {
                    await SendMessage(message.Chat.Id, 3);
                }
                else
                {
                    await SendMessage(message.Chat.Id, 5);
                }
            }
            else if (text.ToLower().StartsWith("повтор ") || text.ToLower().StartsWith("повторяй "))
            {
                await ProcessRecurringTask(chatId, text);
            }
            else
            {
                await ProcessRegularTask(chatId, text);
            }
        }

        private async Task ProcessRegularTask(long chatId, string text)
        {
            string[] lines = text.Split('\n');
            if (lines.Length < 2)
            {
                await SendMessage(chatId, 2);
                return;
            }

            DateTime time;
            if (!CheckFormatDateTime(lines[0].Trim(), out time))
            {
                await SendMessage(chatId, 2);
                return;
            }

            if (time < DateTime.Now)
            {
                await SendMessage(chatId, 3);
                return;
            }

            int userId = await DatabaseManager.GetOrCreateUserAsync(chatId);
            string taskMessage = text.Replace(time.ToString("HH:mm dd.MM.yyyy") + "\n", "");
            await DatabaseManager.AddEventAsync(userId, time, taskMessage);

            await TelegramBotClient.SendMessage(
                chatId,
                $"Задача создана на {time:HH:mm dd.MM.yyyy}");
        }

        private async Task ProcessRecurringTask(long chatId, string text)
        {
            try
            {
                Console.WriteLine($"DEBUG: Получен текст: '{text}'");

                string lowerText = text.ToLower();
                Console.WriteLine($"DEBUG: Текст в нижнем регистре: '{lowerText}'");

                string cleanText = lowerText.StartsWith("повтор ") ?
                    lowerText.Substring("повтор ".Length) :
                    lowerText.StartsWith("повторяй ") ?
                        lowerText.Substring("повторяй ".Length) :
                        lowerText;

                Console.WriteLine($"DEBUG: Очищенный текст: '{cleanText}'");

                string[] parts = cleanText.Split(' ', 3);
                Console.WriteLine($"DEBUG: Частей: {parts.Length}");

                for (int i = 0; i < parts.Length; i++)
                {
                    Console.WriteLine($"DEBUG: Часть {i}: '{parts[i]}'");
                }

                if (parts.Length < 3)
                {
                    await TelegramBotClient.SendMessage(
                        chatId,
                        "Неверный формат. Пример:\n" +
                        "повтор 21:00 среда,воскресенье Полить цветы");
                    return;
                }

                if (!TimeSpan.TryParse(parts[0], out TimeSpan taskTime))
                {
                    Console.WriteLine($"DEBUG: Не удалось распарсить время");
                    await TelegramBotClient.SendMessage(
                        chatId,
                        "Неверный формат времени. Используйте HH:mm");
                    return;
                }
                Console.WriteLine($"DEBUG: Время распознано: {taskTime}");

                string schedulePart = parts[1].ToLower();
                string taskMessage = parts[2];

                Console.WriteLine($"DEBUG: Расписание для парсинга: '{schedulePart}'");
                Console.WriteLine($"DEBUG: Сообщение задачи: '{taskMessage}'");

                string scheduleData = ParseWeekDays(schedulePart);
                Console.WriteLine($"DEBUG: Результат ParseWeekDays: '{scheduleData}'");

                if (string.IsNullOrEmpty(scheduleData))
                {
                    await TelegramBotClient.SendMessage(
                        chatId,
                        "Не удалось распознать дни недели. Используйте: понедельник,вторник,среда...");
                    return;
                }

                Console.WriteLine($"DEBUG: Создаем задачу с данными: сообщение='{taskMessage}', дни='{scheduleData}', время={taskTime}");
                int taskId = await DatabaseManager.CreateRecurringTaskAsync(
                    chatId, taskMessage, scheduleData, taskTime);

                string scheduleInfo = GetScheduleDescription(scheduleData);

                await TelegramBotClient.SendMessage(
                    chatId,
                    $"Создана повторяющаяся задача!\n" +
                    $"Время: {taskTime:hh\\:mm}\n" +
                    $"{scheduleInfo}\n" +
                    $"{taskMessage}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Ошибка: {ex.Message}\n{ex.StackTrace}");
                _logger.LogError(ex, "Ошибка при создании повторяющейся задачи");
                await TelegramBotClient.SendMessage(
                    chatId,
                    "Ошибка при создании повторяющейся задачи. Проверьте формат.");
            }
        }

        private string ParseWeekDays(string input)
        {
            Console.WriteLine($"DEBUG: ParseWeekDays вход: '{input}'");

            var dayMap = new Dictionary<string, int>
            {
                ["понедельник"] = 1,
                ["пн"] = 1,
                ["вторник"] = 2,
                ["вт"] = 2,
                ["среда"] = 3,
                ["ср"] = 3,
                ["четверг"] = 4,
                ["чт"] = 4,
                ["пятница"] = 5,
                ["пт"] = 5,
                ["суббота"] = 6,
                ["сб"] = 6,
                ["воскресенье"] = 0,
                ["вс"] = 0
            };

            var days = input.Split(',')
                .Select(d => d.Trim().ToLower())
                .Where(d => dayMap.ContainsKey(d))
                .Select(d => dayMap[d])
                .Distinct()
                .ToList();

            Console.WriteLine($"DEBUG: Найдено дней: {days.Count}");
            string result = string.Join(",", days.OrderBy(d => d));
            Console.WriteLine($"DEBUG: Результат: '{result}'");

            if (days.Count == 0)
                return "";

            return result;
        }

        private string GetScheduleDescription(string scheduleData)
        {
            var days = scheduleData.Split(',').Select(int.Parse);
            var dayNames = days.Select(d => GetDayName(d)).ToList();
            return "Повторяется по: " + string.Join(", ", dayNames);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message)
            {
                await GetMessage(update.Message);
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQuery(update.CallbackQuery);
            }
        }

        private async Task HandleCallbackQuery(CallbackQuery query)
        {
            try
            {
                if (query.Data.StartsWith("recurring_"))
                {
                    if (int.TryParse(query.Data.Replace("recurring_", ""), out int taskId))
                    {
                        bool success = await DatabaseManager.DeleteRecurringTaskAsync(taskId);
                        if (success)
                        {
                            await TelegramBotClient.AnswerCallbackQuery(
                                callbackQueryId: query.Id,
                                text: "Повторяющаяся задача удалена");

                            await TelegramBotClient.DeleteMessage(
                                chatId: query.Message.Chat.Id,
                                messageId: query.Message.MessageId);
                        }
                    }
                }
                else if (query.Data.StartsWith("del_"))
                {
                    if (int.TryParse(query.Data.Replace("del_", ""), out int taskId))
                    {
                        
                        bool success = await DeleteEventByIdAsync(taskId);
                        if (success)
                        {
                            await TelegramBotClient.AnswerCallbackQuery(
                                callbackQueryId: query.Id,
                                text: "Задача удалена");

                            await TelegramBotClient.DeleteMessage(
                                chatId: query.Message.Chat.Id,
                                messageId: query.Message.MessageId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке callback запроса");
            }
        }

        
        private async Task<bool> DeleteEventByIdAsync(int taskId)
        {
            try
            {
                using var connection = new MySql.Data.MySqlClient.MySqlConnection(ConnectionString);
                await connection.OpenAsync();

                var command = new MySql.Data.MySqlClient.MySqlCommand(
                    "DELETE FROM events WHERE id = @id",
                    connection);
                command.Parameters.AddWithValue("@id", taskId);

                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при удалении задачи с ID {taskId}");
                return false;
            }
        }

        private async Task HandlePollingErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine("Ошибка: " + exception.Message);
        }

        private async void CheckRemindersCallback(object state)
        {
            try
            {
                DateTime currentTime = DateTime.Now;
                Console.WriteLine($"Проверка напоминаний в {currentTime:HH:mm:ss}");

                
                var regularEvents = await DatabaseManager.GetEventsForReminderAsync(currentTime);
                Console.WriteLine($"Найдено обычных задач: {regularEvents.Count}");

                foreach (var ev in regularEvents)
                {
                    Console.WriteLine($"Отправляем напоминание (обычное): {ev.Message}");

                    await TelegramBotClient.SendMessage(
                        ev.TelegramId,
                        "Напоминание: " + ev.Message);
                }

               
                var recurringTasks = await DatabaseManager.GetRecurringTasksForTodayAsync(currentTime);
                Console.WriteLine($"Найдено повторяющихся задач: {recurringTasks.Count}");

                foreach (var task in recurringTasks)
                {
                    if (currentTime.TimeOfDay.Hours == task.Time.Hours &&
                        currentTime.TimeOfDay.Minutes == task.Time.Minutes)
                    {
                        Console.WriteLine($"Отправляем напоминание (повторяющееся): {task.Message}");

                        await TelegramBotClient.SendMessage(
                            task.TelegramId,
                            "Напоминание (повторяющееся): " + task.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке напоминаний: {ex.Message}");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Остановка бота...");

            Timer?.Dispose();

            if (DatabaseManager != null)
            {
                await DatabaseManager.CloseConnectionAsync();
                DatabaseManager.Dispose();
            }

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Бот остановлен");
        }
    }
}