using HistoricalCollector.Worker.Configuration;
using HistoricalCollector.Worker.Infrastructure;
using HistoricalCollector.Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HistoricalDataSettings>(builder.Configuration.GetSection("HistoricalData"));
builder.Services.Configure<GapFillingSettings>(builder.Configuration.GetSection("GapFilling"));
builder.Services.AddHttpClient<BinanceVisionClient>();
builder.Services.AddSingleton<PriceTickBatchRepository>();
builder.Services.AddSingleton<HistoricalIngestionService>();
builder.Services.AddHostedService<HistoricalBackfillWorker>();
builder.Services.AddHostedService<GapFillingWorker>();

var host = builder.Build();
host.Run();
