using TaskManagerTelegramBot_Chernykh.Classes;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TaskManagerTelegramBot_Chernykh
{
    public class Worker : BackgroundService
    {
        readonly string Token = "8362919085:AAGFGKv_nsqLx7iUSlV6J7kgD4U9Mrp_arM";
        readonly string ConnectionString = "Server=localhost;Database=taskmanagerbot;User=root;Password=;";

        ITelegramBotClient TelegramBotClient;
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

        public static InlineKeyboardMarkup DeleteEvent(string Message)
        {
            List<InlineKeyboardButton> inlineKeyboards = new List<InlineKeyboardButton>();
            inlineKeyboards.Add(new InlineKeyboardButton("Удалить", Message));
            return new InlineKeyboardMarkup(inlineKeyboards);
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
                    $"Указанное время и дата не могут быть уставнолены, " +
                    $"потому-что сейчас уже: {DateTime.Now.ToString("HH.mm dd.MM.yyyy")}");
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
                            $"Уведомить пользователя: {task.Time.ToString("HH:mm dd.MM.yyyy")}" +
                            $"\nСообщение: {task.Message}",
                            replyMarkup: DeleteEvent(task.Message)
                        );
                    }
                }
            }
        }

        private async Task GetMessage(Message message)
        {
            Console.WriteLine("Получено сообщение: " + message.Text + " от пользователя: " + message.Chat.Username);
            long IdUser = message.Chat.Id;
            string MessageUser = message.Text;

            if (message.Text.Contains("/"))
                await Command(message.Chat.Id, message.Text);
            else if (message.Text.Equals("Удалить все задачи"))
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
            else
            {
                string[] Info = message.Text.Split('\n');
                if (Info.Length < 2)
                {
                    await SendMessage(message.Chat.Id, 2);
                    return;
                }

                DateTime Time;
                if (CheckFormatDateTime(Info[0], out Time) == false)
                {
                    await SendMessage(message.Chat.Id, 2);
                    return;
                }

                if (Time < DateTime.Now)
                {
                    await SendMessage(message.Chat.Id, 3);
                    return;
                }

                int userId = await DatabaseManager.GetOrCreateUserAsync(message.Chat.Id);

                string taskMessage = message.Text.Replace(Time.ToString("HH:mm dd.MM.yyyy") + "\n", "");
                await DatabaseManager.AddEventAsync(userId, Time, taskMessage);
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message)
            {
                await GetMessage(update.Message);
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                CallbackQuery query = update.CallbackQuery;

                bool success = await DatabaseManager.DeleteEventByMessageAsync(query.Data);
                if (success)
                {
                    await SendMessage(query.Message.Chat.Id, 4);
                }
            }
        }

        private async Task HandlePollingErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine("Ошибка: " + exception.Message);
        }

        private async void CheckRemindersCallback(object obj)
        {
            try
            {
                DateTime currentTime = DateTime.Now;

                var reminders = await DatabaseManager.GetEventsForReminderAsync(
                    new DateTime(currentTime.Year, currentTime.Month, currentTime.Day,
                                currentTime.Hour, currentTime.Minute, 0));

                if (reminders.Count > 0)
                {
                    _logger.LogInformation("Найдено {Count} напоминаний для отправки", reminders.Count);

                    foreach (var reminder in reminders)
                    {
                        try
                        {
                            await TelegramBotClient.SendMessage(
                                chatId: reminder.TelegramId,
                                text: "Напоминание: " + reminder.Message);

                            _logger.LogInformation("Отправлено напоминание пользователю {ChatId}",
                                reminder.TelegramId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка при отправке напоминания пользователю {ChatId}",
                                reminder.TelegramId);
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке напоминаний");
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