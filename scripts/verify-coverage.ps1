param(
    [string[]]$Symbols = @("BTCUSDT","ETHUSDT","BNBUSDT","SOLUSDT","XRPUSDT"),
    [string]$Interval = "1m",
    [datetime]$StartDate = (Get-Date).AddMonths(-12).Date,
    [datetime]$EndDate = (Get-Date).Date,
    [string]$DbHost = "localhost",
    [int]$DbPort = 5433,
    [string]$DbName = "cryptotrading",
    [string]$DbUser = "postgres",
    [string]$DbPassword = "postgres",
    [string]$PsqlPath = ""
)

function Resolve-PsqlPath {
    param([string]$OverridePath)

    if ($OverridePath -and (Test-Path $OverridePath)) {
        return $OverridePath
    }

    $psqlCommand = Get-Command psql -ErrorAction SilentlyContinue
    if ($psqlCommand) {
        return $psqlCommand.Source
    }

    $candidates = @(
        "C:\Program Files\PostgreSQL\18\bin\psql.exe",
        "C:\Program Files\PostgreSQL\17\bin\psql.exe",
        "C:\Program Files\PostgreSQL\16\bin\psql.exe",
        "C:\Program Files\PostgreSQL\15\bin\psql.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "psql was not found. Install PostgreSQL client tools, add psql to PATH, or pass -PsqlPath."
}

$symbolsSql = ($Symbols | ForEach-Object { "'$_'" }) -join ","
$startText = $StartDate.ToString("yyyy-MM-dd")
$endText = $EndDate.ToString("yyyy-MM-dd")

$query = @"
WITH symbols AS (
    SELECT unnest(ARRAY[$symbolsSql]) AS symbol
),
days AS (
    SELECT generate_series(date '$startText', date '$endText', interval '1 day')::date AS day
),
expected AS (
    SELECT s.symbol, d.day
    FROM symbols s
    CROSS JOIN days d
),
actual AS (
    SELECT
        symbol,
        date_trunc('day', time)::date AS day,
        count(*) AS candles
    FROM price_ticks
    WHERE interval = '$Interval'
      AND symbol = ANY(ARRAY[$symbolsSql])
      AND time >= date '$startText'
      AND time < date '$endText' + interval '1 day'
    GROUP BY symbol, date_trunc('day', time)::date
)
SELECT
    e.symbol,
    e.day,
    COALESCE(a.candles, 0) AS actual_candles,
    1440 AS expected_candles,
    ROUND((COALESCE(a.candles, 0) * 100.0 / 1440), 2) AS coverage_percent
FROM expected e
LEFT JOIN actual a ON a.symbol = e.symbol AND a.day = e.day
ORDER BY e.symbol, e.day;
"@

Write-Host "Running coverage query from $startText to $endText for symbols: $($Symbols -join ', ')"
$env:PGPASSWORD = $DbPassword
$psqlExe = Resolve-PsqlPath -OverridePath $PsqlPath
& $psqlExe -h $DbHost -p $DbPort -U $DbUser -d $DbName -c "$query"
$env:PGPASSWORD = ""
