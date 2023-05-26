using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;

namespace TwitterNotification;

class Program
{
        
    private static readonly HttpClient Client = new();
    private static Dictionary<string, string> _lastTweetIds = new();
    private static TelegramBotClient _telegramBotClient = null!;
    private static readonly string ConfigFilePath = "config.json";
    private static readonly string RapidApiKey = "e8442987admshd0a8163b2ed4581p1db1ebjsn930d523ec288";
    private static readonly string TelegramBotToken = "6013028944:AAEHHHbNp4ji1iZhzSqam91rJfv8gwOmPwM";
    private static readonly string TelegramChatId = "-1001892825679";

    static async Task Main()
    {
        // Initialize and start the Telegram bot
        _telegramBotClient = new TelegramBotClient(TelegramBotToken);
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

                    if (!string.IsNullOrEmpty(newTweetId))
                    {
                        _lastTweetIds[username] = newTweetId;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении твита: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
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
                { "X-RapidAPI-Key", RapidApiKey },
                { "X-RapidAPI-Host", "twitter154.p.rapidapi.com" }
            }
        };

        using (var response = await Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            Console.WriteLine(body); // Вывод полного JSON-ответа

            string tweetId = ParseLatestTweetIdFromBody(body);
            string lastTweetId = _lastTweetIds[username];

            if (tweetId != lastTweetId)
            {
                SendTelegramMessage(username, tweetId);
                return tweetId;
            }

            return string.Empty;
        }
    }

    static string ParseLatestTweetIdFromBody(string responseBody)
    {
        var jsonDocument = JsonDocument.Parse(responseBody);
        var resultsArray = jsonDocument.RootElement.GetProperty("results");

        if (resultsArray.GetArrayLength() > 0)
        {
            var tweetId = resultsArray[0].GetProperty("tweet_id").GetString(); // Получение tweetId
            return tweetId;
        }

        return string.Empty;
    }

    static async Task SendTelegramMessage(string username, string tweetId)
    {
        string tweetUrl = $"https://twitter.com/{username}/status/{tweetId}";
        string message = $"@{username}{Environment.NewLine}Открыть твит: {tweetUrl}";

        await _telegramBotClient.SendTextMessageAsync(TelegramChatId, message);
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