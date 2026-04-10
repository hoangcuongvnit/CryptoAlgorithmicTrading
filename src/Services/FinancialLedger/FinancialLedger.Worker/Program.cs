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
builder.Services.AddScoped<EquitySnapshotRepository>();
builder.Services.AddScoped<SessionManagementService>();
builder.Services.AddScoped<PnlCalculationService>();
builder.Services.AddScoped<SessionResetSagaService>();
builder.Services.AddScoped<EquitySellSnapshotService>();

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

app.MapGet("/api/ledger/balance/effective", async (
	string? environment,
	string? baseCurrency,
	VirtualAccountRepository accountRepository,
	SessionManagementService sessionService,
	PnlCalculationService pnlService,
	CancellationToken ct) =>
{
	var env = string.IsNullOrWhiteSpace(environment)
		? ledgerSettings.DefaultEnvironment
		: environment.ToUpperInvariant();
	var currency = string.IsNullOrWhiteSpace(baseCurrency)
		? "USDT"
		: baseCurrency.ToUpperInvariant();

	var accountId = await accountRepository.GetOrCreateAccountAsync(env, currency);
	var activeSession = await sessionService.GetActiveSessionAsync(accountId);

	if (activeSession is null)
	{
		await sessionService.CreateSessionAsync(accountId, ledgerSettings.DefaultAlgorithmName, ledgerSettings.DefaultInitialBalance);
		activeSession = await sessionService.GetActiveSessionAsync(accountId);
	}

	if (activeSession is null)
	{
		return Results.Ok(new
		{
			environment = env,
			baseCurrency = currency,
			source = "FINANCIAL_LEDGER",
			available = false,
			balance = (decimal?)null,
			asOfUtc = (DateTime?)null,
			detail = "No active session available for effective balance lookup."
		});
	}

	var currentBalance = await pnlService.GetCurrentBalanceAsync(activeSession.Id);

	return Results.Ok(new
	{
		environment = env,
		baseCurrency = currency,
		source = "FINANCIAL_LEDGER",
		available = true,
		balance = currentBalance,
		asOfUtc = DateTime.UtcNow,
		accountId,
		sessionId = activeSession.Id,
		activeSession.InitialBalance
	});
});

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

app.MapGet("/api/ledger/equity/sell-timeline", async (
	string sessionId,
	DateTime? fromDate,
	DateTime? toDate,
	int? limit,
	EquitySnapshotRepository snapshotRepository,
	CancellationToken ct) =>
{
	if (!Guid.TryParse(sessionId, out var parsedSessionId))
	{
		return Results.BadRequest("Invalid session ID");
	}

	var take = Math.Clamp(limit ?? 1000, 1, 5000);
	var points = await snapshotRepository.GetSellTimelineAsync(parsedSessionId, fromDate, toDate, take);
	var firstPoint = points.FirstOrDefault();
	var latestPoint = points.LastOrDefault();

	object? summary = null;
	if (firstPoint is not null && latestPoint is not null)
	{
		var deltaValue = latestPoint.TotalEquity - firstPoint.TotalEquity;
		decimal? deltaPercent = null;

		if (firstPoint.TotalEquity != 0m)
		{
			deltaPercent = decimal.Round((deltaValue / firstPoint.TotalEquity) * 100m, 4);
		}

		summary = new
		{
			firstEquity = firstPoint.TotalEquity,
			latestEquity = latestPoint.TotalEquity,
			deltaValue = deltaValue,
			deltaPercent,
			firstSellAt = firstPoint.SnapshotTime,
			latestSellAt = latestPoint.SnapshotTime,
		};
	}

	return Results.Ok(new
	{
		SessionId = parsedSessionId,
		Total = points.Count,
		Summary = summary,
		Points = points,
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

	var openPositions = await sagaService.GetOpenPositionsCountAsync(ct);
	if (openPositions > 0 && !request.ConfirmCloseAll)
	{
		return Results.Conflict(new
		{
			requiresConfirmation = true,
			openPositions,
			message = "Open positions detected. Confirm close-all to start a clean ledger session."
		});
	}

	if (openPositions > 0)
	{
		var requestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "ledger" : request.RequestedBy;
		var closeAllResult = await sagaService.RequestCloseAllAndWaitAsync(parsedAccountId, requestedBy!, ct);
		if (!closeAllResult.Success)
		{
			return Results.Conflict(new
			{
				requiresConfirmation = false,
				openPositions,
				closeAllFailed = true,
				message = closeAllResult.Message,
				closedCount = closeAllResult.ClosedCount
			});
		}
	}

	var newSessionId = await sessionService.ResetSessionAsync(
		parsedAccountId,
		request.NewInitialBalance,
		request.AlgorithmName);

	return Results.Created($"/api/ledger/sessions/{parsedAccountId}", new
	{
		AccountId = parsedAccountId,
		NewSessionId = newSessionId,
		OpenPositionsClosed = openPositions,
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

app.MapGet("/api/ledger/binance-account", async (
	IHttpClientFactory httpClientFactory,
	CancellationToken ct) =>
{
	try
	{
		var client = httpClientFactory.CreateClient("executor");
		var response = await client.GetAsync("/api/trading/spot-account", ct);
		var body = await response.Content.ReadAsStringAsync(ct);
		return response.IsSuccessStatusCode
			? Results.Content(body, "application/json", System.Text.Encoding.UTF8, (int)response.StatusCode)
			: Results.Problem($"Executor returned {(int)response.StatusCode}: {body}");
	}
	catch (Exception ex)
	{
		return Results.Problem($"Failed to reach Executor: {ex.Message}");
	}
});

await app.RunAsync();
