using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Executor.API.Services;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Create metrics instance first (before OTEL config)
var metrics = new OrderExecutionMetrics();

// OpenTelemetry - Tracing
var tracingOtel = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter();
    })
    .WithMetrics(metricsBuilder =>
    {
        metricsBuilder
            .AddMeter(metrics.GetMeter().Name)
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter();
    });

// Add services to the container.
builder.Services.AddGrpc();

builder.Services.Configure<TradingSettings>(builder.Configuration.GetSection("Trading"));
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));

var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
	var config = ConfigurationOptions.Parse(redisConnection);
	config.AbortOnConnectFail = false;
	return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddSingleton<IBinanceRestClient>(_ => new BinanceRestClient());

builder.Services.AddSingleton<PriceReferenceRepository>();
builder.Services.AddSingleton<OrderRepository>();
builder.Services.AddSingleton<AuditStreamPublisher>();
builder.Services.AddSingleton<PaperOrderSimulator>();
builder.Services.AddSingleton<BinanceOrderClient>();
builder.Services.AddSingleton(metrics);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<OrderExecutorGrpcService>();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Executor.API" }));
app.MapGet("/metrics", () => "Prometheus metrics endpoint. Configure Prometheus to scrape http://localhost:9091/metrics");
app.MapGet("/", () => "Order Executor gRPC service is running.");

app.Run();
