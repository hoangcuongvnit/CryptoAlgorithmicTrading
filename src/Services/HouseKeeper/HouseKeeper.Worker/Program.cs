using HouseKeeper.Worker;
using HouseKeeper.Worker.Configuration;
using HouseKeeper.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HouseKeeperSettings>(
    builder.Configuration.GetSection("HouseKeeper"));

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

// Register jobs
builder.Services.AddSingleton<ICleanupJob>(sp =>
    new DataGapsCleanupJob(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HouseKeeperSettings>>(),
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DataGapsCleanupJob>>(),
        connectionString));

builder.Services.AddSingleton<ICleanupJob>(sp =>
    new OrdersArchiveJob(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HouseKeeperSettings>>(),
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OrdersArchiveJob>>(),
        connectionString));

builder.Services.AddSingleton<ICleanupJob>(sp =>
    new PriceTicksPartitionJob(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HouseKeeperSettings>>(),
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PriceTicksPartitionJob>>(),
        connectionString));

builder.Services.AddSingleton<ICleanupJob>(sp =>
    new UnusedTableAuditJob(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UnusedTableAuditJob>>(),
        connectionString));

// Main worker (receives all ICleanupJob registrations via IEnumerable<ICleanupJob>)
builder.Services.AddSingleton(connectionString);
builder.Services.AddHostedService<HouseKeeperWorker>();

var host = builder.Build();
host.Run();
