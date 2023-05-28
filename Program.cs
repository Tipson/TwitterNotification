using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Configuration;

namespace TwitterNotification;

class Program
{
    private static readonly HttpClient Client = new();
    private static Dictionary<string, string> _lastTweetIds = new();
    private static TelegramBotClient _telegramBotClient = null!;
    private static IConfiguration? Configuration { get; set; }
    private static string _rapidApiKey = string.Empty;
    private static string _telegramChatId = string.Empty;

    static async Task Main()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        Configuration = builder.Build();

        _rapidApiKey = Configuration["RapidApiKey"];
        string telegramBotToken = Configuration["TelegramBotToken"];
        _telegramChatId = Configuration["TelegramChatId"];
        string configFilePath = Configuration["ConfigFilePath"];

        _telegramBotClient = new TelegramBotClient(telegramBotToken);
        _telegramBotClient.OnMessage += Bot_OnMessage;
        _telegramBotClient.StartReceiving();

        await LoadConfig(configFilePath);

        while (true)
        {
            await UpdateTweets(_rapidApiKey);
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }

    static async Task UpdateTweets(string rapidApiKey)
    {
        foreach (var username in _lastTweetIds.Keys.ToList())
        {
            try
            {
                string? newTweetId = await GetLatestTweetId(username, rapidApiKey);

                if (!string.IsNullOrEmpty(newTweetId) && newTweetId != _lastTweetIds[username])
                {
                    _lastTweetIds[username] = newTweetId;
                    await SendTelegramMessage(username, newTweetId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении твита: {ex.Message}");
            }
        }
    }

    static async Task<string> GetLatestTweetId(string username, string rapidApiKey)
    {
        string url = $"https://twitter154.p.rapidapi.com/user/tweets?username={username}&limit=3&include_replies=false&include_pinned=false";

        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(url),
            Headers =
            {
                { "X-RapidAPI-Key", rapidApiKey },
                { "X-RapidAPI-Host", "twitter154.p.rapidapi.com" }
            }
        };

        using (var response = await Client.SendAsync(request))
        {
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to get tweets for {username}. Status code: {response.StatusCode}. Body: {body}");
                return string.Empty;
            }

            var jsonDocument = JsonDocument.Parse(body);
            var resultsArray = jsonDocument.RootElement.GetProperty("results");

            if (resultsArray.GetArrayLength() > 0)
            {
                if (resultsArray[0].TryGetProperty("tweet_id", out var tweetIdProperty))
                {
                    string tweetId = tweetIdProperty.GetString() ?? string.Empty;

                    // Add pretty print output
                    string tweetUrl = $"https://twitter.com/{username}/status/{tweetId}";
                    Console.WriteLine($"New tweet from {username}: {tweetUrl}");

                    return tweetId;
                }

                if (resultsArray[0].TryGetProperty("text", out var tweetTextProperty))
                {
                    Console.WriteLine($"Tweet text: {tweetTextProperty.GetString()}");
                }
            }
            return string.Empty;
        }
    }

    static async Task SendTelegramMessage(string username, string tweetId)
    {
        string tweetUrl = $"https://twitter.com/{username}/status/{tweetId}";
        string message = $"@{username}{Environment.NewLine}Открыть твит: {tweetUrl}";

        await _telegramBotClient.SendTextMessageAsync(_telegramChatId, message);
    }

    static async void Bot_OnMessage(object? sender, MessageEventArgs e)
    {
        var message = e.Message;
        string configFilePath = Configuration["ConfigFilePath"];

        if (message.Type == MessageType.Text)
        {
            if (message.Text.StartsWith("/add"))
            {
                string username = message.Text.Substring(4).Trim();
                if (!_lastTweetIds.ContainsKey(username))
                {
                    _lastTweetIds[username] = string.Empty;
                    await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, $"Аккаунт {username} добавлен в список отслеживаемых.");
                    await SaveConfig(configFilePath);
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
                    await SaveConfig(configFilePath);
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

    static async Task LoadConfig(string configFilePath)
    {
        try
        {
            if (File.Exists(configFilePath))
            {
                string json = await File.ReadAllTextAsync(configFilePath);
                _lastTweetIds = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при загрузке конфигурации: {ex.Message}");
        }
    }

    static async Task SaveConfig(string configFilePath)
    {
        try
        {
            string json = JsonSerializer.Serialize(_lastTweetIds);
            await File.WriteAllTextAsync(configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при сохранении конфигурации: {ex.Message}");
        }
    }
}
