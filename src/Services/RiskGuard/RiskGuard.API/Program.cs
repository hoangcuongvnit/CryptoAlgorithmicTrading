using RiskGuard.API.Configuration;
using RiskGuard.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.Configure<RiskSettings>(builder.Configuration.GetSection("Risk"));
builder.Services.AddSingleton<RiskValidationEngine>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<RiskGuardGrpcService>();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "RiskGuard.API" }));
app.MapGet("/", () => "RiskGuard gRPC service is running.");

app.Run();
