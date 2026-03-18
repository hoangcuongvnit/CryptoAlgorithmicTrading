const defaultSymbols = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"];
const state = {
    symbols: [...defaultSymbols],
    interval: "1m",
    startUtc: null,
    endUtc: null,
    mainSymbol: "BTCUSDT",
    liveMode: false,
    refreshSeconds: 20,
    workbenchPage: 1,
    workbenchPageSize: 100,
    workbench: { columns: [], rows: [], totalRows: 0, pageNumber: 1, pageSize: 100 },
    gapsPage: 1,
    gapsPageSize: 50,
    gaps: { data: [], totalRows: 0, pageNumber: 1, pageSize: 50 }
};

const charts = {};
let autoRefreshHandle = null;

function toInputDateTime(date) {
    const pad = (value) => value.toString().padStart(2, "0");
    return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}T${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
}

function fromInputDateTime(value) {
    return new Date(value + ":00Z");
}

function showToast(message) {
    const toast = document.getElementById("toast");
    toast.textContent = message;
    toast.classList.add("show");
    setTimeout(() => toast.classList.remove("show"), 2000);
}

function buildQuery(params) {
    const query = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        if (value === undefined || value === null) {
            continue;
        }

        if (Array.isArray(value)) {
            for (const item of value) {
                query.append(key, item);
            }
            continue;
        }

        query.set(key, value);
    }

    return query;
}

async function apiGet(path, params) {
    const query = buildQuery(params);
    const response = await fetch(`${path}?${query.toString()}`);
    if (!response.ok) {
        const text = await response.text();
        throw new Error(`API ${response.status}: ${text}`);
    }
    return await response.json();
}

function initControls() {
    const now = new Date();
    const start = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);

    state.startUtc = start;
    state.endUtc = now;

    const symbolsSelect = document.getElementById("symbols");
    const mainSymbolSelect = document.getElementById("mainSymbol");

    for (const symbol of defaultSymbols) {
        const option = new Option(symbol, symbol, true, true);
        symbolsSelect.append(option);
    }

    for (const symbol of defaultSymbols) {
        const option = new Option(symbol, symbol, symbol === state.mainSymbol, symbol === state.mainSymbol);
        mainSymbolSelect.append(option);
    }

    document.getElementById("interval").value = state.interval;
    document.getElementById("startUtc").value = toInputDateTime(state.startUtc);
    document.getElementById("endUtc").value = toInputDateTime(state.endUtc);
    document.getElementById("liveMode").checked = state.liveMode;
    document.getElementById("refreshSeconds").value = String(state.refreshSeconds);

    document.getElementById("applyBtn").addEventListener("click", async () => {
        readStateFromControls();
        state.workbenchPage = 1;
        state.gapsPage = 1;
        await loadAll();
    });

    document.getElementById("refreshBtn").addEventListener("click", async () => {
        readStateFromControls();
        await loadAll();
    });

    document.getElementById("liveMode").addEventListener("change", () => {
        readStateFromControls();
        updateAutoRefreshTimer();
    });

    document.getElementById("refreshSeconds").addEventListener("change", () => {
        readStateFromControls();
        updateAutoRefreshTimer();
    });

    document.getElementById("runTemplateBtn").addEventListener("click", async () => {
        state.workbenchPage = 1;
        await loadWorkbench();
    });

    document.getElementById("workbenchPrevBtn").addEventListener("click", async () => {
        if (state.workbenchPage <= 1) {
            return;
        }

        state.workbenchPage--;
        await loadWorkbench();
    });

    document.getElementById("workbenchNextBtn").addEventListener("click", async () => {
        const totalPages = Math.max(1, Math.ceil((state.workbench.totalRows || 0) / state.workbenchPageSize));
        if (state.workbenchPage >= totalPages) {
            return;
        }

        state.workbenchPage++;
        await loadWorkbench();
    });

    document.getElementById("gapsPrevBtn").addEventListener("click", async () => {
        if (state.gapsPage <= 1) {
            return;
        }

        state.gapsPage--;
        await loadGaps();
    });

    document.getElementById("gapsNextBtn").addEventListener("click", async () => {
        const totalPages = Math.max(1, Math.ceil((state.gaps.totalRows || 0) / state.gapsPageSize));
        if (state.gapsPage >= totalPages) {
            return;
        }

        state.gapsPage++;
        await loadGaps();
    });

    document.getElementById("exportCsvBtn").addEventListener("click", exportWorkbenchCsv);

    document.querySelectorAll("#tabs .nav-link").forEach((button) => {
        button.addEventListener("click", () => {
            document.querySelectorAll("#tabs .nav-link").forEach((node) => node.classList.remove("active"));
            button.classList.add("active");

            const tab = button.dataset.tab;
            document.querySelectorAll(".tab-panel").forEach((panel) => panel.classList.remove("active"));
            document.getElementById(`tab-${tab}`).classList.add("active");

            resizeCharts();
        });
    });
}

function readStateFromControls() {
    const symbolsSelect = document.getElementById("symbols");
    state.symbols = Array.from(symbolsSelect.selectedOptions).map((node) => node.value);
    if (state.symbols.length === 0) {
        state.symbols = [...defaultSymbols];
    }

    state.mainSymbol = document.getElementById("mainSymbol").value || state.symbols[0] || defaultSymbols[0];
    state.interval = document.getElementById("interval").value;
    state.startUtc = fromInputDateTime(document.getElementById("startUtc").value);
    state.endUtc = fromInputDateTime(document.getElementById("endUtc").value);
    state.liveMode = document.getElementById("liveMode").checked;
    state.refreshSeconds = Number(document.getElementById("refreshSeconds").value || "20");
}

function chart(id) {
    if (!charts[id]) {
        charts[id] = echarts.init(document.getElementById(id));
    }
    return charts[id];
}

function resizeCharts() {
    Object.values(charts).forEach((instance) => instance.resize());
}

window.addEventListener("resize", resizeCharts);

function isoUtc(date) {
    return date.toISOString();
}

function updateAutoRefreshTimer() {
    if (autoRefreshHandle) {
        clearInterval(autoRefreshHandle);
        autoRefreshHandle = null;
    }

    if (!state.liveMode) {
        return;
    }

    autoRefreshHandle = setInterval(async () => {
        try {
            await loadAll(false);
        } catch {
        }
    }, Math.max(5, state.refreshSeconds) * 1000);
}

async function loadAll(showRefreshToast = true) {
    try {
        const params = {
            startUtc: isoUtc(state.startUtc),
            endUtc: isoUtc(state.endUtc),
            interval: state.interval,
            symbols: state.symbols
        };

        const [overview, candles, quality, schema] = await Promise.all([
            apiGet("/api/dashboard/overview", params),
            apiGet("/api/dashboard/candles", { ...params, symbol: state.mainSymbol }),
            apiGet("/api/dashboard/quality/coverage", params),
            apiGet("/api/dashboard/schema", {})
        ]);

        renderOverview(overview);
        renderMarket(candles);
        renderQuality(quality);
        renderSchema(schema);
        await loadGaps();
        await loadWorkbench();
        await loadRisk();
        await loadNotifier();
        if (showRefreshToast) {
            showToast("Dashboard refreshed");
        }
    } catch (error) {
        console.error(error);
        showToast(error.message);
    }
}

async function loadRisk() {
    try {
        const [config, stats] = await Promise.all([
            apiGet("/api/risk/config", {}),
            apiGet("/api/risk/stats", {})
        ]);
        renderRisk(config, stats);
    } catch (error) {
        console.error("Failed to load risk data:", error);
        document.getElementById("riskCards").innerHTML =
            `<div class="col-12"><div class="alert alert-warning mb-0">Risk Guard unavailable: ${escapeHtml(error.message)}</div></div>`;
    }
}

function renderRisk(config, stats) {
    // ── Metric cards ──────────────────────────────────────────────────────
    const cards = document.getElementById("riskCards");
    cards.innerHTML = "";

    const pnlColor = stats.dailyPnl >= 0 ? "#22c55e" : "#ef4444";
    const cardData = [
        ["Daily P&L", `<span style="color:${pnlColor}">${stats.dailyPnl >= 0 ? "+" : ""}${Number(stats.dailyPnl).toFixed(2)} USDT</span>`],
        ["Today Approved", stats.todayApproved],
        ["Today Rejected", stats.todayRejected],
        ["Active Cooldowns", (stats.cooldowns || []).length]
    ];

    for (const [title, value] of cardData) {
        const col = document.createElement("div");
        col.className = "col-6 col-lg-3";
        col.innerHTML = `<div class="metric-card"><div class="text-secondary small">${title}</div><div class="metric-value">${value}</div></div>`;
        cards.appendChild(col);
    }

    // ── Drawdown bar ──────────────────────────────────────────────────────
    const pct = Math.min(100, Math.max(0, Number(stats.drawdownUsedPercent) || 0));
    const bar = document.getElementById("riskDrawdownBar");
    bar.style.width = `${pct}%`;
    bar.className = `progress-bar ${pct >= 80 ? "bg-danger" : pct >= 50 ? "bg-warning" : "bg-success"}`;
    document.getElementById("riskDrawdownLabel").textContent =
        `${pct}% of ${Number(stats.maxDrawdownUsd).toFixed(0)} USDT max`;

    // ── Config table ──────────────────────────────────────────────────────
    const configRows = [
        ["Min Risk/Reward", config.minRiskReward],
        ["Max Order Notional", `${config.maxOrderNotional} USDT`],
        ["Max Position Size", `${config.maxPositionSizePercent}%`],
        ["Virtual Balance", `${config.virtualAccountBalance} USDT`],
        ["Max Drawdown", `${config.maxDrawdownPercent}%`],
        ["Cooldown", `${config.cooldownSeconds}s`],
        ["Paper Trading Only", config.paperTradingOnly ? "Yes" : "No"],
        ["Allowed Symbols", (config.allowedSymbols || []).join(", ") || "All"]
    ];

    const configTable = document.getElementById("riskConfigTable");
    configTable.innerHTML = "";
    for (const [label, value] of configRows) {
        const tr = document.createElement("tr");
        tr.innerHTML = `<td class="text-secondary small">${label}</td><td class="fw-medium">${value}</td>`;
        configTable.appendChild(tr);
    }

    // ── Cooldowns table ───────────────────────────────────────────────────
    const cooldowns = stats.cooldowns || [];
    document.getElementById("riskCooldownCount").textContent = cooldowns.length;

    const cooldownsTable = document.getElementById("riskCooldownsTable");
    const cooldownsEmpty = document.getElementById("riskCooldownsEmpty");
    cooldownsTable.innerHTML = "";

    if (cooldowns.length === 0) {
        cooldownsEmpty.style.display = "";
    } else {
        cooldownsEmpty.style.display = "none";
        for (const c of cooldowns) {
            const tr = document.createElement("tr");
            tr.innerHTML = `<td>${c.symbol}</td><td class="text-muted small">${new Date(c.lastOrderUtc).toISOString().slice(11, 19)}</td><td><span class="badge bg-warning text-dark">${c.remainingSeconds}s</span></td>`;
            cooldownsTable.appendChild(tr);
        }
    }

    // ── Today decisions donut chart ───────────────────────────────────────
    chart("riskDecisionsChart").setOption({
        title: { text: "Today's Decisions", left: "center" },
        tooltip: { trigger: "item" },
        series: [{
            type: "pie",
            radius: ["40%", "70%"],
            data: [
                { value: stats.todayApproved, name: "Approved", itemStyle: { color: "#22c55e" } },
                { value: stats.todayRejected, name: "Rejected", itemStyle: { color: "#ef4444" } }
            ],
            label: { formatter: "{b}: {c}" }
        }]
    }, true);

    // ── Validation history table ──────────────────────────────────────────
    const historyTable = document.getElementById("riskHistoryTable");
    historyTable.innerHTML = "";
    for (const r of (stats.recentValidations || [])) {
        const tr = document.createElement("tr");
        const badge = r.approved
            ? `<span class="badge bg-success">Approved</span>`
            : `<span class="badge bg-danger">Rejected</span>`;
        tr.innerHTML = `<td class="text-muted small">${new Date(r.timestampUtc).toISOString().replace("T", " ").slice(0, 19)}</td><td>${r.symbol}</td><td>${r.side}</td><td>${badge}</td><td class="text-muted small">${escapeHtml(r.rejectionReason || "")}</td>`;
        historyTable.appendChild(tr);
    }
}

async function loadGaps() {
    try {
        const params = {
            startUtc: isoUtc(state.startUtc),
            endUtc: isoUtc(state.endUtc),
            interval: state.interval,
            symbols: state.symbols,
            page: state.gapsPage,
            pageSize: state.gapsPageSize
        };

        const result = await apiGet("/api/dashboard/quality/gaps", params);
        state.gaps = result;

        const table = document.getElementById("gapsTable");
        table.innerHTML = "";
        for (const gap of result.data || []) {
            const tr = document.createElement("tr");
            tr.innerHTML = `<td>${gap.symbol}</td><td>${new Date(gap.gapStart).toISOString()}</td><td>${new Date(gap.gapEnd).toISOString()}</td><td>${gap.durationMinutes}</td><td>${gap.filledAt ? new Date(gap.filledAt).toISOString() : "-"}</td>`;
            table.appendChild(tr);
        }

        const totalPages = Math.max(1, Math.ceil((result.totalRows || 0) / state.gapsPageSize));
        const pageInfo = document.getElementById("gapsPageInfo");
        pageInfo.textContent = `Page ${result.pageNumber || 1} of ${totalPages} (${result.totalRows || 0} total)`;

        const prevBtn = document.getElementById("gapsPrevBtn");
        const nextBtn = document.getElementById("gapsNextBtn");
        prevBtn.disabled = (result.pageNumber || 1) <= 1;
        nextBtn.disabled = (result.pageNumber || 1) >= totalPages;
    } catch (error) {
        console.error("Failed to load gaps:", error);
    }
}

function renderOverview(data) {
    const cards = document.getElementById("overviewCards");
    cards.innerHTML = "";

    let totalRows = 0;
    for (const row of data.symbols) {
        totalRows += Number(row.rowCount);
    }

    const cardData = [
        ["Total Symbols", data.symbols.length],
        ["Total Rows", totalRows.toLocaleString()],
        ["Open Gaps", data.totalOpenGaps],
        ["Range", `${new Date(data.startUtc).toISOString().slice(0, 10)} → ${new Date(data.endUtc).toISOString().slice(0, 10)}`]
    ];

    for (const [title, value] of cardData) {
        const col = document.createElement("div");
        col.className = "col-6 col-lg-3";
        col.innerHTML = `<div class="metric-card"><div class="text-secondary small">${title}</div><div class="metric-value">${value}</div></div>`;
        cards.appendChild(col);
    }

    const symbolNames = data.symbols.map((item) => item.symbol);
    const coverageValues = data.symbols.map((item) => item.coveragePercent);
    chart("coverageChart").setOption({
        title: { text: "Coverage % by Symbol" },
        tooltip: { trigger: "axis" },
        xAxis: { type: "category", data: symbolNames },
        yAxis: { type: "value", min: 0, max: 100 },
        series: [{ type: "bar", data: coverageValues, itemStyle: { color: "#3b82f6" } }]
    });

    const freshness = data.symbols.map((item) => Number.isFinite(item.freshnessMinutes) ? item.freshnessMinutes : 9999);
    chart("freshnessChart").setOption({
        title: { text: "Freshness Lag (minutes)" },
        tooltip: { trigger: "axis" },
        xAxis: { type: "category", data: symbolNames },
        yAxis: { type: "value" },
        series: [{ type: "bar", data: freshness, itemStyle: { color: "#f59e0b" } }]
    });
}

function renderMarket(data) {
    const dates = data.candles.map((item) => item.time);
    const klineData = data.candles.map((item) => [item.open, item.close, item.low, item.high]);
    const volumeData = data.candles.map((item) => item.volume);

    chart("candlesChart").setOption({
        title: { text: `Candlestick (${data.symbol}, ${data.interval})` },
        tooltip: { trigger: "axis" },
        axisPointer: { link: [{ xAxisIndex: [0, 1] }] },
        grid: [{ left: 60, right: 20, height: "58%" }, { left: 60, right: 20, top: "72%", height: "18%" }],
        xAxis: [
            { type: "category", data: dates, boundaryGap: false },
            { type: "category", data: dates, gridIndex: 1, boundaryGap: false }
        ],
        yAxis: [{ scale: true }, { scale: true, gridIndex: 1 }],
        dataZoom: [{ type: "inside", xAxisIndex: [0, 1] }, { show: true, xAxisIndex: [0, 1], type: "slider", bottom: 5 }],
        series: [
            { name: "KLine", type: "candlestick", data: klineData },
            { name: "Volume", type: "bar", xAxisIndex: 1, yAxisIndex: 1, data: volumeData }
        ]
    }, true);

    const grouped = new Map();
    for (const item of data.comparison) {
        if (!grouped.has(item.symbol)) {
            grouped.set(item.symbol, []);
        }
        grouped.get(item.symbol).push([item.time, item.close]);
    }

    const comparisonSeries = [];
    for (const [symbol, values] of grouped.entries()) {
        comparisonSeries.push({ name: symbol, type: "line", showSymbol: false, data: values });
    }

    chart("comparisonChart").setOption({
        title: { text: "Close Price Comparison" },
        tooltip: { trigger: "axis" },
        legend: { type: "scroll" },
        xAxis: { type: "time" },
        yAxis: { type: "value", scale: true },
        series: comparisonSeries
    }, true);

    const closes = data.candles.map((item) => Number(item.close));
    const volData = rollingVolatility(dates, closes, 30);
    chart("volatilityChart").setOption({
        title: { text: "Rolling Volatility (30 periods)" },
        tooltip: { trigger: "axis" },
        xAxis: { type: "time" },
        yAxis: { type: "value" },
        series: [{ type: "line", showSymbol: false, data: volData, areaStyle: {} }]
    }, true);

    const drawdownData = drawdownSeries(dates, closes);
    chart("drawdownChart").setOption({
        title: { text: "Drawdown" },
        tooltip: { trigger: "axis" },
        xAxis: { type: "time" },
        yAxis: { type: "value", axisLabel: { formatter: (value) => `${value}%` } },
        series: [{ type: "line", showSymbol: false, data: drawdownData, areaStyle: {} }]
    }, true);
}

function rollingVolatility(times, closes, windowSize) {
    const returns = [];
    for (let i = 1; i < closes.length; i++) {
        const previous = closes[i - 1];
        const current = closes[i];
        if (previous > 0) {
            returns.push((current - previous) / previous);
        } else {
            returns.push(0);
        }
    }

    const result = [];
    for (let i = 0; i < returns.length; i++) {
        const start = Math.max(0, i - windowSize + 1);
        const slice = returns.slice(start, i + 1);
        const avg = slice.reduce((acc, value) => acc + value, 0) / slice.length;
        const variance = slice.reduce((acc, value) => acc + Math.pow(value - avg, 2), 0) / slice.length;
        const sigma = Math.sqrt(variance);
        result.push([times[i + 1], sigma]);
    }

    return result;
}

function drawdownSeries(times, closes) {
    let peak = Number.NEGATIVE_INFINITY;
    const result = [];

    for (let i = 0; i < closes.length; i++) {
        const close = closes[i];
        if (close > peak) {
            peak = close;
        }
        const drawdown = peak > 0 ? ((close - peak) / peak) * 100 : 0;
        result.push([times[i], Number(drawdown.toFixed(4))]);
    }

    return result;
}

function renderQuality(data) {
    const bySymbol = new Map();
    for (const row of data.dailyCoverage) {
        if (!bySymbol.has(row.symbol)) {
            bySymbol.set(row.symbol, []);
        }
        bySymbol.get(row.symbol).push([new Date(`${row.day}T00:00:00Z`).toISOString(), row.actualCandles]);
    }

    const series = [];
    for (const [symbol, values] of bySymbol.entries()) {
        series.push({ name: symbol, type: "line", showSymbol: false, data: values });
    }

    chart("dailyCoverageChart").setOption({
        title: { text: "Daily Candle Count by Symbol" },
        tooltip: { trigger: "axis" },
        legend: { type: "scroll" },
        xAxis: { type: "time" },
        yAxis: { type: "value" },
        series
    }, true);

    chart("gapHistogramChart").setOption({
        title: { text: "Gap Duration Histogram" },
        tooltip: { trigger: "axis" },
        xAxis: { type: "category", data: data.gapDurationHistogram.map((item) => item.label) },
        yAxis: { type: "value" },
        series: [{ type: "bar", data: data.gapDurationHistogram.map((item) => item.count), itemStyle: { color: "#ef4444" } }]
    }, true);
}

function renderSchema(data) {
    const tables = document.getElementById("schemaTables");
    tables.innerHTML = "";
    for (const item of data.tables) {
        const li = document.createElement("li");
        li.className = "list-group-item d-flex justify-content-between";
        li.innerHTML = `<span>${item.tableName}</span><span class="badge bg-secondary">${item.tableType}</span>`;
        tables.appendChild(li);
    }

    const columns = document.getElementById("schemaColumns");
    columns.innerHTML = "";
    for (const column of data.columns.slice(0, 300)) {
        const tr = document.createElement("tr");
        tr.innerHTML = `<td>${column.tableName}</td><td>${column.columnName}</td><td>${column.dataType}</td><td>${column.isNullable ? "YES" : "NO"}</td>`;
        columns.appendChild(tr);
    }

    const indexes = document.getElementById("schemaIndexes");
    indexes.innerHTML = "";
    for (const index of data.indexes) {
        const tr = document.createElement("tr");
        tr.innerHTML = `<td>${index.tableName}</td><td>${index.indexName}</td><td><code>${escapeHtml(index.indexDefinition)}</code></td>`;
        indexes.appendChild(tr);
    }
}

function escapeHtml(text) {
    return text
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

async function loadWorkbench() {
    const template = document.getElementById("templateSelect").value;
    const result = await apiGet(`/api/dashboard/workbench/template/${template}`, {
        startUtc: isoUtc(state.startUtc),
        endUtc: isoUtc(state.endUtc),
        interval: state.interval,
        symbols: state.symbols,
        page: state.workbenchPage,
        pageSize: state.workbenchPageSize
    });

    state.workbench = {
        columns: result.columns,
        rows: result.rows,
        totalRows: result.totalRows,
        pageNumber: result.pageNumber,
        pageSize: result.pageSize
    };

    const head = document.getElementById("workbenchHead");
    const body = document.getElementById("workbenchBody");

    head.innerHTML = "";
    body.innerHTML = "";

    const trHead = document.createElement("tr");
    for (const column of result.columns) {
        const th = document.createElement("th");
        th.textContent = column;
        trHead.appendChild(th);
    }
    head.appendChild(trHead);

    for (const row of result.rows) {
        const tr = document.createElement("tr");
        for (const column of result.columns) {
            const td = document.createElement("td");
            const value = row[column];
            td.textContent = value === null || value === undefined ? "" : String(value);
            tr.appendChild(td);
        }
        body.appendChild(tr);
    }

    const totalPages = Math.max(1, Math.ceil((result.totalRows || 0) / Math.max(1, result.pageSize || state.workbenchPageSize)));
    state.workbenchPage = result.pageNumber;
    document.getElementById("workbenchPageInfo").textContent = `Page ${result.pageNumber}/${totalPages} • Rows ${result.totalRows}`;
    document.getElementById("workbenchPrevBtn").disabled = result.pageNumber <= 1;
    document.getElementById("workbenchNextBtn").disabled = result.pageNumber >= totalPages;
}

function exportWorkbenchCsv() {
    const { columns, rows } = state.workbench;
    if (!columns.length) {
        showToast("No workbench data to export");
        return;
    }

    const lines = [columns.join(",")];
    for (const row of rows) {
        const values = columns.map((column) => {
            const raw = row[column] ?? "";
            const escaped = String(raw).replaceAll('"', '""');
            return `"${escaped}"`;
        });
        lines.push(values.join(","));
    }

    const blob = new Blob([lines.join("\n")], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `workbench-${Date.now()}.csv`;
    link.click();
    URL.revokeObjectURL(url);
}

async function loadNotifier() {
    try {
        const [config, stats] = await Promise.all([
            apiGet("/api/notifier/config", {}),
            apiGet("/api/notifier/stats", {})
        ]);
        renderNotifier(config, stats);
    } catch (error) {
        console.error("Failed to load notifier data:", error);
        document.getElementById("notifierCards").innerHTML =
            `<div class="col-12"><div class="alert alert-warning mb-0">Notifier unavailable: ${escapeHtml(error.message)}</div></div>`;
    }
}

function renderNotifier(config, stats) {
    // ── Metric cards ──────────────────────────────────────────────────────
    const cards = document.getElementById("notifierCards");
    cards.innerHTML = "";

    const telegramBadge = config.telegramEnabled
        ? `<span class="badge bg-success">Connected</span>`
        : `<span class="badge bg-secondary">Disabled</span>`;

    const cardData = [
        ["Telegram", telegramBadge],
        ["Today Total", stats.todayTotal || 0],
        ["Orders", (stats.todayByCategory || {})["order"] || 0],
        ["System Events", (stats.todayByCategory || {})["system_event"] || 0]
    ];

    for (const [title, value] of cardData) {
        const col = document.createElement("div");
        col.className = "col-6 col-lg-3";
        col.innerHTML = `<div class="metric-card"><div class="text-secondary small">${title}</div><div class="metric-value">${value}</div></div>`;
        cards.appendChild(col);
    }

    // ── Config table ──────────────────────────────────────────────────────
    const configRows = [
        ["Telegram", config.telegramEnabled ? "Enabled" : "Disabled"],
        ["Bot Configured", config.botConfigured ? "Yes" : "No"],
        ["Chat ID", config.chatId],
        ["Redis", config.redisConnection],
        ["History Capacity", config.historyCapacity]
    ];

    const configTable = document.getElementById("notifierConfigTable");
    configTable.innerHTML = "";
    for (const [label, value] of configRows) {
        const tr = document.createElement("tr");
        tr.innerHTML = `<td class="text-secondary small">${label}</td><td class="fw-medium">${value}</td>`;
        configTable.appendChild(tr);
    }

    // ── Badge ─────────────────────────────────────────────────────────────
    document.getElementById("notifierTotalBadge").textContent = `${stats.todayTotal || 0} today`;

    // ── Category donut chart ──────────────────────────────────────────────
    const byCategory = stats.todayByCategory || {};
    const categoryColors = {
        startup: "#6366f1",
        order: "#22c55e",
        order_rejected: "#ef4444",
        system_event: "#f59e0b"
    };

    const pieData = Object.entries(byCategory).map(([name, value]) => ({
        name,
        value,
        itemStyle: { color: categoryColors[name] || "#94a3b8" }
    }));

    chart("notifierCategoryChart").setOption({
        title: { text: "Today by Category", left: "center" },
        tooltip: { trigger: "item" },
        series: [{
            type: "pie",
            radius: ["40%", "70%"],
            data: pieData.length > 0 ? pieData : [{ name: "No data", value: 1, itemStyle: { color: "#e2e8f0" } }],
            label: { formatter: "{b}: {c}" }
        }]
    }, true);

    // ── Timeline bar chart (by hour bucket) ──────────────────────────────
    const recentNotifications = stats.recentNotifications || [];
    const hourBuckets = {};
    for (const r of recentNotifications) {
        const hour = new Date(r.timestampUtc).getUTCHours();
        hourBuckets[hour] = (hourBuckets[hour] || 0) + 1;
    }

    const hours = Array.from({ length: 24 }, (_, i) => i);
    const hourCounts = hours.map(h => hourBuckets[h] || 0);

    chart("notifierTimelineChart").setOption({
        title: { text: "Activity by Hour (UTC)", left: "center" },
        tooltip: { trigger: "axis" },
        xAxis: { type: "category", data: hours.map(h => `${h}:00`), axisLabel: { rotate: 45 } },
        yAxis: { type: "value", minInterval: 1 },
        series: [{ type: "bar", data: hourCounts, itemStyle: { color: "#6366f1" } }]
    }, true);

    // ── Notification history table ────────────────────────────────────────
    const historyTable = document.getElementById("notifierHistoryTable");
    historyTable.innerHTML = "";

    const categoryBadgeClass = {
        startup: "bg-primary",
        order: "bg-success",
        order_rejected: "bg-danger",
        system_event: "bg-warning text-dark"
    };

    for (const r of recentNotifications) {
        const tr = document.createElement("tr");
        const badgeClass = categoryBadgeClass[r.category] || "bg-secondary";
        tr.innerHTML = `<td class="text-muted small">${new Date(r.timestampUtc).toISOString().replace("T", " ").slice(0, 19)}</td><td><span class="badge ${badgeClass}">${r.category}</span></td><td>${escapeHtml(r.summary)}</td>`;
        historyTable.appendChild(tr);
    }

    if (recentNotifications.length === 0) {
        const tr = document.createElement("tr");
        tr.innerHTML = `<td colspan="3" class="text-muted text-center">No notifications recorded yet</td>`;
        historyTable.appendChild(tr);
    }
}

async function bootstrap() {
    initControls();
    updateAutoRefreshTimer();
    await loadAll();
}

bootstrap();