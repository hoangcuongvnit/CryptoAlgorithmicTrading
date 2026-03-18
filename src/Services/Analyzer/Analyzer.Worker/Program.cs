using Analyzer.Worker.Analysis;
using Analyzer.Worker.Configuration;
using Analyzer.Worker.Infrastructure;
using Analyzer.Worker.Workers;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Services.Configure<AnalyzerSettings>(builder.Configuration.GetSection("Analyzer"));

// Redis
var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// Analysis pipeline
builder.Services.AddSingleton<PriceBuffer>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AnalyzerSettings>>().Value;
    return new PriceBuffer(settings.BufferCapacity);
});
builder.Services.AddSingleton<IndicatorEngine>();
builder.Services.AddSingleton<SignalPublisher>();

// Workers
builder.Services.AddHostedService<SignalAnalyzerWorker>();

var host = builder.Build();
host.Run();
