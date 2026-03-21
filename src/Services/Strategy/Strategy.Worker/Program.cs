using CryptoTrading.Executor.Grpc;
using CryptoTrading.RiskGuard.Grpc;
using CryptoTrading.Shared.Session;
using Grpc.Net.Client;
using StackExchange.Redis;
using Strategy.Worker;
using Strategy.Worker.Configuration;
using Strategy.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<GrpcEndpoints>(builder.Configuration.GetSection("Grpc"));
builder.Services.Configure<TradingSettings>(builder.Configuration.GetSection("Trading"));
builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection("Trading:Session"));
builder.Services.AddSingleton<SessionClock>();
builder.Services.AddSingleton<SessionTradingPolicy>();

var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

builder.Services.AddSingleton(sp =>
{
    var endpoints = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GrpcEndpoints>>().Value;
    var channel = GrpcChannel.ForAddress(endpoints.RiskGuardUrl);
    return new RiskGuardService.RiskGuardServiceClient(channel);
});

builder.Services.AddSingleton(sp =>
{
    var endpoints = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GrpcEndpoints>>().Value;
    var channel = GrpcChannel.ForAddress(endpoints.ExecutorUrl);
    return new OrderExecutorService.OrderExecutorServiceClient(channel);
});
builder.Services.AddSingleton<SignalToOrderMapper>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
