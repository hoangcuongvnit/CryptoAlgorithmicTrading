using Notifier.Worker.Configuration;
using Notifier.Worker.Channels;
using Notifier.Worker.Workers;
using StackExchange.Redis;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));

// Redis
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// Telegram
builder.Services.AddSingleton<TelegramNotifier>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<TelegramSettings>>().Value;
    var logger = sp.GetRequiredService<ILogger<TelegramNotifier>>();
    return new TelegramNotifier(settings.BotToken, settings.ChatId, logger);
});

// Workers
builder.Services.AddHostedService<NotifierWorker>();

var host = builder.Build();
host.Run();
