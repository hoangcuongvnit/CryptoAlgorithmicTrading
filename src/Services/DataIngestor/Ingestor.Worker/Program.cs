using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using CryptoExchange.Net.Authentication;
using Ingestor.Worker.Configuration;
using Ingestor.Worker.Infrastructure;
using Ingestor.Worker.Workers;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Services.Configure<TradingSettings>(builder.Configuration.GetSection("Trading"));
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<PersistenceSettings>(builder.Configuration.GetSection("Persistence"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));

// Redis
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// Binance clients — environment (testnet vs live) is determined from config at startup
builder.Services.AddSingleton<IBinanceSocketClient>(sp =>
{
    var s = sp.GetRequiredService<IOptions<BinanceSettings>>().Value;
    return new BinanceSocketClient(opts =>
    {
        opts.Environment = s.UseTestnet
            ? BinanceEnvironment.Testnet
            : BinanceEnvironment.Live;
    });
});

builder.Services.AddSingleton<IBinanceRestClient>(sp =>
{
    var s = sp.GetRequiredService<IOptions<BinanceSettings>>().Value;
    return new BinanceRestClient(opts =>
    {
        opts.Environment = s.UseTestnet
            ? BinanceEnvironment.Testnet
            : BinanceEnvironment.Live;
        if (!string.IsNullOrEmpty(s.ActiveApiKey))
            opts.ApiCredentials = new ApiCredentials(s.ActiveApiKey, s.ActiveApiSecret);
    });
});

// Infrastructure
builder.Services.AddSingleton<RedisPublisher>();
builder.Services.AddSingleton<PriceTickWriteQueue>();
builder.Services.AddSingleton<PriceTickRepository>();

// Workers
builder.Services.AddHostedService<BinanceIngestorWorker>();
builder.Services.AddHostedService<SymbolConfigListener>();
builder.Services.AddHostedService<PriceTickPersistenceWorker>();

var host = builder.Build();
host.Run();

