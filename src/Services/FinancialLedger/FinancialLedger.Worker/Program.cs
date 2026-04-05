using FinancialLedger.Worker.Configuration;
using FinancialLedger.Worker.Domain;
using FinancialLedger.Worker.Hubs;
using FinancialLedger.Worker.Infrastructure;
using FinancialLedger.Worker.Services;
using FinancialLedger.Worker.Workers;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var ledgerSettings = new LedgerSettings();
builder.Configuration.GetSection("Ledger").Bind(ledgerSettings);
builder.Services.AddSingleton(ledgerSettings);

var redisConnection = builder.Configuration.GetValue<string>("Redis:Connection") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

builder.Services.AddSignalR();

builder.Services.AddScoped<VirtualAccountRepository>();
builder.Services.AddScoped<LedgerRepository>();
builder.Services.AddScoped<SessionManagementService>();
builder.Services.AddScoped<PnlCalculationService>();
builder.Services.AddScoped<SessionResetSagaService>();

builder.Services.AddHostedService<TradeEventConsumerWorker>();

// Equity projection: HttpClient for Executor + background worker
builder.Services.AddHttpClient("executor", client =>
{
    if (!string.IsNullOrWhiteSpace(ledgerSettings.ExecutorUrl))
    {
        client.BaseAddress = new Uri(ledgerSettings.ExecutorUrl);
    }
});
builder.Services.AddHostedService<EquityProjectionWorker>();

var app = builder.Build();

app.MapHub<LedgerHub>("/ledger-hub");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "FinancialLedger.Worker" }));

app.MapGet("/api/ledger/account/{accountId}", async (
	string accountId,
	SessionManagementService sessionService,
	PnlCalculationService pnlService,
	CancellationToken ct) =>
{
	if (!Guid.TryParse(accountId, out var parsedAccountId))
	{
		return Results.BadRequest("Invalid account ID");
	}

	var activeSession = await sessionService.GetActiveSessionAsync(parsedAccountId);
	if (activeSession is null)
	{
		return Results.NotFound("No active session found");
	}

	var currentBalance = await pnlService.GetCurrentBalanceAsync(activeSession.Id);
	var netPnl = await pnlService.CalculateNetPnlAsync(activeSession.Id);
	var roePercent = PnlCalculationService.CalculateRoePercent(netPnl, activeSession.InitialBalance);

	return Results.Ok(new
	{
		activeSession.Id,
		activeSession.AccountId,
		activeSession.AlgorithmName,
		activeSession.InitialBalance,
		CurrentBalance = currentBalance,
		NetPnl = netPnl,
		RoePercent = roePercent,
		Timestamp = DateTime.UtcNow,
	});
});

app.MapGet("/api/ledger/entries", async (
	string sessionId,
	DateTime? fromDate,
	DateTime? toDate,
	string? symbol,
	string? type,
	int page,
	int pageSize,
	LedgerRepository ledgerRepository,
	CancellationToken ct) =>
{
	if (!Guid.TryParse(sessionId, out var parsedSessionId))
	{
		return Results.BadRequest("Invalid session ID");
	}

	if (page < 1)
	{
		page = 1;
	}

	pageSize = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 500);

	var (entries, total) = await ledgerRepository.GetLedgerEntriesAsync(
		parsedSessionId,
		fromDate,
		toDate,
		symbol,
		type,
		page,
		pageSize);

	return Results.Ok(new
	{
		SessionId = parsedSessionId,
		Total = total,
		Page = page,
		PageSize = pageSize,
		Entries = entries,
	});
});

app.MapGet("/api/ledger/sessions/{accountId}", async (
	string accountId,
	string? status,
	SessionManagementService sessionService,
	CancellationToken ct) =>
{
	if (!Guid.TryParse(accountId, out var parsedAccountId))
	{
		return Results.BadRequest("Invalid account ID");
	}

	var sessions = await sessionService.GetSessionsAsync(parsedAccountId, status);
	return Results.Ok(sessions);
});

app.MapGet("/api/ledger/pnl", async (
	string sessionId,
	string? symbol,
	PnlCalculationService pnlService,
	CancellationToken ct) =>
{
	if (!Guid.TryParse(sessionId, out var parsedSessionId))
	{
		return Results.BadRequest("Invalid session ID");
	}

	var breakdown = await pnlService.GetPnlBreakdownAsync(parsedSessionId);

	if (string.IsNullOrWhiteSpace(symbol))
	{
		return Results.Ok(breakdown);
	}

	return breakdown.TryGetValue(symbol, out var perSymbol)
		? Results.Ok(new Dictionary<string, PnlBreakdown> { [symbol] = perSymbol })
		: Results.Ok(new Dictionary<string, PnlBreakdown>());
});

app.MapPost("/api/ledger/sessions/reset", async (
	ResetSessionRequest request,
	VirtualAccountRepository accountRepository,
	SessionManagementService sessionService,
	SessionResetSagaService sagaService,
	CancellationToken ct) =>
{
	if (!Guid.TryParse(request.AccountId, out var parsedAccountId))
	{
		return Results.BadRequest("Invalid account ID");
	}

	if (request.NewInitialBalance <= 0)
	{
		return Results.BadRequest("NewInitialBalance must be greater than zero");
	}

	var account = await accountRepository.GetAccountAsync(parsedAccountId);
	if (account is null)
	{
		return Results.NotFound("Account not found");
	}

	await sagaService.RequestHaltAndCloseAllAsync(parsedAccountId, ct);
	var newSessionId = await sessionService.ResetSessionAsync(
		parsedAccountId,
		request.NewInitialBalance,
		request.AlgorithmName);

	return Results.Created($"/api/ledger/sessions/{parsedAccountId}", new
	{
		AccountId = parsedAccountId,
		NewSessionId = newSessionId,
		request.NewInitialBalance,
	});
});

app.MapPost("/api/ledger/accounts/bootstrap", async (
	string? environment,
	string? baseCurrency,
	VirtualAccountRepository accountRepository,
	SessionManagementService sessionService,
	CancellationToken ct) =>
{
	var env = string.IsNullOrWhiteSpace(environment) ? ledgerSettings.DefaultEnvironment : environment.ToUpperInvariant();
	var currency = string.IsNullOrWhiteSpace(baseCurrency) ? "USDT" : baseCurrency.ToUpperInvariant();

	var accountId = await accountRepository.GetOrCreateAccountAsync(env, currency);
	var activeSession = await sessionService.GetActiveSessionAsync(accountId);

	if (activeSession is null)
	{
		await sessionService.CreateSessionAsync(accountId, ledgerSettings.DefaultAlgorithmName, ledgerSettings.DefaultInitialBalance);
		activeSession = await sessionService.GetActiveSessionAsync(accountId);
	}

	return Results.Ok(new
	{
		AccountId = accountId,
		Environment = env,
		BaseCurrency = currency,
		ActiveSession = activeSession,
	});
});

await app.RunAsync();
