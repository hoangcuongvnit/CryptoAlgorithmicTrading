using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using Ingestor.Worker.Configuration;
using Ingestor.Worker.Infrastructure;
using Ingestor.Worker.Workers;
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

// Binance clients
// NOTE: Socket client always uses Live environment — Binance testnet does NOT provide
// WebSocket market data streams (klines/tickers). Only REST order execution uses testnet.
builder.Services.AddSingleton<IBinanceSocketClient>(sp =>
{
    return new BinanceSocketClient(opts =>
    {
        opts.Environment = BinanceEnvironment.Live;
    });
});

// REST client always uses Live — market data queries (exchange info, server time, etc.)
// do not need testnet. Only order execution in Executor uses testnet.
builder.Services.AddSingleton<IBinanceRestClient>(sp =>
{
    return new BinanceRestClient(opts =>
    {
        opts.Environment = BinanceEnvironment.Live;
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

