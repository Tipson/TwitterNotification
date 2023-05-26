using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using File = System.IO.File;

namespace TwitterNotification;

class Program
{
    private static readonly HttpClient Client = new();
    private static Dictionary<string, string> _lastTweetIds = new();
    private static TelegramBotClient _telegramBotClient = null!;
    private static readonly string ConfigFilePath = "config.json";

    static async Task Main(string[] args)
    {
        // Initialize and start the Telegram bot
        _telegramBotClient = new TelegramBotClient("6013028944:AAEHHHbNp4ji1iZhzSqam91rJfv8gwOmPwM");
        _telegramBotClient.OnMessage += Bot_OnMessage;
        _telegramBotClient.StartReceiving();

        await LoadConfig();

        while (true)
        {
            try
            {
                foreach (var username in _lastTweetIds.Keys.ToList())
                {
                    string newTweetId = await GetLatestTweetId(username);

                    if (string.IsNullOrEmpty(_lastTweetIds[username]) || newTweetId != _lastTweetIds[username])
                    {
                        string message = $"Новый твит опубликован от аккаунта {username}!";
                        Console.WriteLine(message);
                        await SendTelegramMessage(message); // Отправка уведомления в Telegram
                        _lastTweetIds[username] = newTweetId;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении твита: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(100)); // Пауза в 10 секунд перед следующей проверкой
        }
    }

    static async Task<string> GetLatestTweetId(string username)
    {
        string url = $"https://twitter154.p.rapidapi.com/user/tweets?username={username}&limit=1&include_replies=false&include_pinned=false";

        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(url),
            Headers =
            {
                { "X-RapidAPI-Key", "e8442987admshd0a8163b2ed4581p1db1ebjsn930d523ec288" },
                { "X-RapidAPI-Host", "twitter154.p.rapidapi.com" }
            }
        };

        using (var response = await Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            string tweetId = ParseLatestTweetIdFromBody(body);
            return tweetId;
        }
    }

    static string ParseLatestTweetIdFromBody(string responseBody)
    {
        int startIndex = responseBody.IndexOf("\"tweet_id\":") + 12;
        int endIndex = responseBody.IndexOf("\"", startIndex);
        string tweetId = responseBody.Substring(startIndex, endIndex - startIndex);
        return tweetId;
    }

    static async Task SendTelegramMessage(string message)
    {
        // Отправка сообщения в Telegram
        await _telegramBotClient.SendTextMessageAsync("-1001892825679", message);
    }

    static async void Bot_OnMessage(object? sender, MessageEventArgs e)
    {
        // Обработка входящих сообщений от Telegram бота
        var message = e.Message;
        if (message.Type == MessageType.Text)
        {
            if (message.Text.StartsWith("/add"))
            {
                string username = message.Text.Substring(4).Trim();
                if (!_lastTweetIds.ContainsKey(username))
                {
                    _lastTweetIds[username] = string.Empty;
                    await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, $"Аккаунт {username} добавлен в список отслеживаемых.");
                    await SaveConfig();
                }
                else
                {
                    await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, $"Аккаунт {username} уже присутствует в списке отслеживаемых.");
                }
            }
            else if (message.Text.StartsWith("/remove"))
            {
                string username = message.Text.Substring(7).Trim();
                if (_lastTweetIds.ContainsKey(username))
                {
                    _lastTweetIds.Remove(username);
                    await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, $"Аккаунт {username} удален из списка отслеживаемых.");
                    await SaveConfig();
                }
                else
                {
                    await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, $"Аккаунт {username} не найден в списке отслеживаемых.");
                }
            }
            else if (message.Text == "/list")
            {
                string userList = string.Join(Environment.NewLine, _lastTweetIds.Keys);
                string response = string.IsNullOrEmpty(userList) ? "Список отслеживаемых аккаунтов пуст." : $"Отслеживаемые аккаунты:{Environment.NewLine}{userList}";
                await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, response);
            }
        }
    }

    static async Task LoadConfig()
    {
        if (File.Exists(ConfigFilePath))
        {
            string json = await File.ReadAllTextAsync(ConfigFilePath);
            _lastTweetIds = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
    }

    static async Task SaveConfig()
    {
        string json = JsonSerializer.Serialize(_lastTweetIds);
        await File.WriteAllTextAsync(ConfigFilePath, json);
    }
}