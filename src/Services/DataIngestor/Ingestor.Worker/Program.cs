using Ingestor.Worker.Configuration;
using Ingestor.Worker.Infrastructure;
using Ingestor.Worker.Workers;
using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Services.Configure<TradingSettings>(builder.Configuration.GetSection("Trading"));
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<PersistenceSettings>(builder.Configuration.GetSection("Persistence"));

// Redis
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// Binance clients
builder.Services.AddSingleton<IBinanceSocketClient>(sp =>
{
    var client = new BinanceSocketClient();
    return client;
});

builder.Services.AddSingleton<IBinanceRestClient>(sp =>
{
    var client = new BinanceRestClient();
    return client;
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

